type FriendStatus = { accountId: string; online?: boolean }

/** A present realtime false is authoritative; only missing presence uses the snapshot. */
export function isFriendOnline(friend: FriendStatus, presence: ReadonlyMap<string, { online?: boolean }>): boolean {
  return presence.get(friend.accountId)?.online ?? Boolean(friend.online)
}

/** Stable linear partition: never mutate resource order or conversation selection. */
export function onlineFirstFriends<T extends FriendStatus>(friends: readonly T[], presence: ReadonlyMap<string, { online?: boolean }>): T[] {
  const online: T[] = [], offline: T[] = []
  for (const friend of friends) (isFriendOnline(friend, presence) ? online : offline).push(friend)
  return online.concat(offline)
}
