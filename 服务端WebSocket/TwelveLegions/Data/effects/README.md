# Legion12 卡效原子定义

`atoms.schema.json` 是卡效组合的版本化数据契约。运行时目录按下列边界组织：

- `S01/`、`S02/`：普通卡、军团、圣物与战术。
- `masters/`：主宰能力。
- `factions/`：印刷在士气卡上的阵营效果。
- `disasters/`：天灾及最终天灾。
- `special/`：试炼、符文、神力、晋升、卡诺匹斯与衍生牌。

迁移期间，卡面原文会先由 `L12AtomicEffectCatalog` 保守映射。无法唯一确定或尚未通过
逐卡等价测试的能力必须包含 `legacy.resolve`，继续使用现有权威分支。只有完成状态快照、
Prompt、费用、公开信息、日志和触发顺序的等价测试后，才能改为 `runtime-migrated` 或
`verified`；不得仅因“后台已经能画出节点”就删除旧实现。
