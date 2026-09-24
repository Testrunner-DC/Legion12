namespace TwelveLegions.Server;

public sealed record L12TestRunAcceptanceFixtureSummary(string Owner, int Decks, int PublicDecks,
    int GuidedDecks);

public sealed partial class L12PlatformStore
{
    private const string AcceptanceDeckPrefix = "[验收] ";

    internal L12TestRunAcceptanceFixtureSummary EnsureTestRunAcceptanceFixtures()
    {
        var templates = _officialDecks
            .Where(deck => !string.IsNullOrWhiteSpace(deck.MasterId) && deck.CardIds.Count > 0)
            .GroupBy(deck => deck.MasterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (templates.Length == 0)
            return new L12TestRunAcceptanceFixtureSummary("unavailable", 0, 0, 0);

        var horizontal = templates.FirstOrDefault(deck => deck.SpecialIds.Count > 0) ?? templates[0];
        var standard = templates.FirstOrDefault(deck => !string.Equals(deck.MasterId, horizontal.MasterId,
            StringComparison.OrdinalIgnoreCase)) ?? horizontal;
        var alternate = templates.FirstOrDefault(deck => !string.Equals(deck.MasterId, horizontal.MasterId,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(deck.MasterId, standard.MasterId, StringComparison.OrdinalIgnoreCase)) ?? standard;

        var desired = new[]
        {
            FixtureDeck(standard, $"{AcceptanceDeckPrefix}可删除牌库"),
            FixtureDeck(alternate, $"{AcceptanceDeckPrefix}指南与对局建议"),
            FixtureDeck(horizontal, $"{AcceptanceDeckPrefix}横卡与超长名称牌库用于编号卡名截断验收"),
        };

        string ownerId;
        string ownerName;
        lock (_gate)
        {
            var owner = _data.Accounts.FirstOrDefault(account => !account.Deleted && !account.Disabled
                    && string.Equals(account.Username, "Aimin", StringComparison.OrdinalIgnoreCase))
                ?? _data.Accounts.First(account => !account.Deleted && !account.Disabled
                    && string.Equals(account.Username, "Admin", StringComparison.Ordinal));
            ownerId = owner.Id;
            ownerName = PublicUsername(owner);
            var changed = false;
            foreach (var deck in desired)
            {
                if (_data.Decks.Any(row => row.AccountId == ownerId
                        && string.Equals(row.Name, deck.Name, StringComparison.OrdinalIgnoreCase))) continue;
                _data.Decks.Add(new DeckRow
                {
                    AccountId = ownerId,
                    Name = deck.Name,
                    MasterId = deck.MasterId,
                    CardIds = deck.CardIds.ToList(),
                    MoraleIds = deck.MoraleIds.ToList(),
                    SpecialIds = deck.SpecialIds.ToList(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                changed = true;
            }
            if (changed) Save();
        }

        var guided = 0;
        foreach (var deck in desired.Skip(1))
        {
            var publication = PublishedDecks(ownerId).FirstOrDefault(row =>
                    string.Equals(row.Deck.Name, deck.Name, StringComparison.OrdinalIgnoreCase))
                ?? PublishDeck(ownerId, deck, publicationId: null)
                ?? throw new InvalidOperationException($"无法建立测试服验收公开牌库：{deck.Name}");
            var current = PublicDeckDetails(publication.Id);
            if ((current?.ContentRevision ?? 0) == 0)
            {
                var opponents = templates.Where(row => !string.Equals(row.MasterId, deck.MasterId,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToArray();
                var matchups = opponents.Select((opponent, index) => new L12PublicDeckMatchupView(
                    opponent.MasterId,
                    index == 0
                        ? "验收数据：记录先后手差异、关键交换和资源保留时点；用于验证长内容不会挤压卡牌区域。"
                        : "验收数据：对方展开较快时优先稳定场面，详情页应完整显示本段文字。",
                    "验收数据：点击卡名仍应使用图鉴的统一卡牌详情。",
                    "验收数据：按实际对局环境替换，不作为正式构筑建议。"
                )).ToArray();
                UpdatePublicDeckContent(ownerId, publication.Id, new L12PublicDeckContentInput(
                    new L12PublicDeckGuideView(
                        "这是测试服验收用构筑思路。用于检查同页章节、长文本换行、锚点滚动和移动端阅读，不代表正式攻略。",
                        "优先保留能够建立基础场面的卡牌；这里用于验证起手建议编辑、保存和再次打开。",
                        "关键牌名称应可点击并打开与图鉴一致的详情，不应产生第二套变形卡面。",
                        "按资源、登场、进攻顺序记录常见展开；滚动定位后上下文不能丢失。",
                        "替换建议用于检查空内容与长内容边界，测试数据不会进入正式服。"),
                    matchups));
            }
            if ((PublicDeckDetails(publication.Id)?.ContentRevision ?? 0) > 0) guided++;

            lock (_gate)
            {
                var row = _data.PublishedDecks.First(item => item.Id == publication.Id);
                row.Views = Math.Max(row.Views, deck.SpecialIds.Count > 0 ? 96 : 48);
                row.Copies = Math.Max(row.Copies, deck.SpecialIds.Count > 0 ? 12 : 7);
                if (!row.LikedByAccountIds.Contains(ownerId, StringComparer.OrdinalIgnoreCase))
                    row.LikedByAccountIds.Add(ownerId);
                Save();
            }
        }

        return new L12TestRunAcceptanceFixtureSummary(ownerName,
            Decks(ownerId).Count(deck => deck.Name.StartsWith(AcceptanceDeckPrefix, StringComparison.Ordinal)),
            PublishedDecks(ownerId).Count(deck => deck.Deck.Name.StartsWith(AcceptanceDeckPrefix,
                StringComparison.Ordinal)), guided);
    }

    private static L12PresetDeckDefinition FixtureDeck(L12PresetDeckDefinition source, string name)
        => new()
        {
            Name = name,
            MasterId = source.MasterId,
            CardIds = source.CardIds.ToList(),
            MoraleIds = source.MoraleIds.ToList(),
            SpecialIds = source.SpecialIds.ToList(),
        };
}
