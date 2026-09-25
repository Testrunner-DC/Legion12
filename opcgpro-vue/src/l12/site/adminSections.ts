export const adminDomains = [
  { id: 'workbench', label: '工作台' }, { id: 'users', label: '用户服务' },
  { id: 'content', label: '内容与卡牌' }, { id: 'matches', label: '对局与赛事' },
  { id: 'operations', label: '游戏运营' }, { id: 'system', label: '系统治理' },
] as const

export const adminSections = [
  { id: 'overview', label: '工作台', domain: 'workbench', path: '/admin', icon: '▦', permissions: [] },
  { id: 'accounts', label: '账号', domain: 'users', path: '/admin/users/accounts', icon: '♙', permissions: ['admin.accounts.read'] },
  { id: 'username-requests', label: '改名审核', domain: 'users', path: '/admin/users/renames', icon: '✎', permissions: ['admin.accounts.read'] },
  { id: 'bugs', label: 'Bug 闭环', domain: 'users', path: '/admin/users/bugs', icon: '⚑', permissions: ['admin.bugs.read'] },
  { id: 'content', label: '站点内容', domain: 'content', path: '/admin/content/site', icon: '▤', permissions: ['admin.content.read'] },
  { id: 'rules', label: '规则审核', domain: 'content', path: '/admin/content/rules', icon: '§', permissions: ['admin.content.read'] },
  { id: 'effects', label: '卡效工作台', domain: 'content', path: '/admin/content/effects', icon: '◇', permissions: ['admin.effects.read'] },
  { id: 'alternate-arts', label: '异画与权益', domain: 'content', path: '/admin/content/alternate-arts', icon: '✦', permissions: ['admin.content.read'] },
  { id: 'matches', label: '对局档案', domain: 'matches', path: '/admin/matches/archive', icon: '▣', permissions: ['admin.matches.read'] },
  { id: 'match-governance', label: '对局治理', domain: 'matches', path: '/admin/matches/governance', icon: '⚖', permissions: ['admin.match-governance.read'] },
  { id: 'integrity', label: '排位完整性', domain: 'matches', path: '/admin/matches/integrity', icon: '◆', permissions: ['admin.audit.read'] },
  { id: 'tournaments', label: '赛事管理', domain: 'matches', path: '/admin/matches/tournaments', icon: '♜', permissions: ['tournaments.manage', 'tournaments.rulings.write'] },
  { id: 'operations', label: '运营配置', domain: 'operations', path: '/admin/operations/config', icon: '⚙', permissions: ['admin.operations.read'] },
  { id: 'global-data', label: '全局数据', domain: 'operations', path: '/admin/operations/global', icon: '⌁', permissions: ['admin.analytics.read'] },
  { id: 'card-analytics', label: '卡牌数据', domain: 'operations', path: '/admin/operations/cards', icon: '◈', permissions: ['admin.analytics.read'] },
  { id: 'releases', label: '软件发布', domain: 'system', path: '/admin/system/releases', icon: '⇧', permissions: ['releases.read', 'releases.runtime.read'] },
  { id: 'security', label: '安全状态', domain: 'system', path: '/admin/system/security', icon: '◆', permissions: ['admin.security.read'] },
  { id: 'storage', label: '服务健康与存储', domain: 'system', path: '/admin/system/storage', icon: '▤', permissions: ['admin.security.read'] },
  { id: 'commands', label: '操作记录', domain: 'system', path: '/admin/system/commands', icon: '⌁', permissions: ['admin.commands.read'] },
  { id: 'audit', label: '审计日志', domain: 'system', path: '/admin/system/audit', icon: '≡', permissions: ['admin.audit.read'] },
] as const
export type AdminTab = typeof adminSections[number]['id']
export type AdminDomain = typeof adminDomains[number]['id']
export function visibleAdminSections(allowed: (permission: string) => boolean) {
  return adminSections.filter(item => !item.permissions.length || item.permissions.some(allowed))
}
export function adminSection(id: string | undefined) { return adminSections.find(item => item.id === id) }
