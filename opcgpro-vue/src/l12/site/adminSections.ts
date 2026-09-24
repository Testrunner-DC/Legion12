export const adminSections = [
  { id: 'overview', label: '后台概览', group: '总览', icon: '▦', permissions: [] },
  { id: 'accounts', label: '账号与会话', group: '用户与反馈', icon: '♙', permissions: ['admin.accounts.read'] },
  { id: 'username-requests', label: '改名审核', group: '用户与反馈', icon: '✎', permissions: ['admin.accounts.read'] },
  { id: 'bugs', label: 'Bug 管理', group: '用户与反馈', icon: '⚑', permissions: ['admin.bugs.read'] },
  { id: 'matches', label: '对局档案', group: '数据管理', icon: '▣', permissions: ['admin.matches.read'] },
  { id: 'match-governance', label: '对局治理', group: '数据管理', icon: '⚖', permissions: ['admin.match-governance.read'] },
  { id: 'global-data', label: '全局数据', group: '数据管理', icon: '⌁', permissions: ['admin.analytics.read'] },
  { id: 'card-analytics', label: '卡牌数据', group: '数据管理', icon: '◈', permissions: ['admin.analytics.read'] },
  { id: 'content', label: '站点内容工作台', group: '站点内容', icon: '▤', permissions: ['admin.content.read'] },
  { id: 'rules', label: '规则中心审核', group: '站点内容', icon: '§', permissions: ['admin.content.read'] },
  { id: 'alternate-arts', label: '异画管理与权益', group: '收藏与权益', icon: '✦', permissions: ['admin.content.read'] },
  { id: 'operations', label: '游戏运营配置', group: '游戏与赛事运营', icon: '⚙', permissions: ['admin.operations.read'] },
  { id: 'tournaments', label: '赛事管理', group: '游戏与赛事运营', icon: '♜', permissions: ['tournaments.manage', 'tournaments.rulings.write'] },
  { id: 'commands', label: '管理操作记录', group: '游戏与赛事运营', icon: '⌁', permissions: ['admin.commands.read'] },
  { id: 'effects', label: '卡效统一工作台', group: '卡牌与规则', icon: '◇', permissions: ['admin.effects.read'] },
  { id: 'releases', label: '软件发布', group: '系统与治理', icon: '⇧', permissions: ['releases.read', 'releases.runtime.read'] },
  { id: 'security', label: '安全状态', group: '系统与治理', icon: '◆', permissions: ['admin.security.read'] },
  { id: 'storage', label: '服务器存储', group: '系统与治理', icon: '▤', permissions: ['admin.security.read'] },
  { id: 'integrity', label: '排位完整性', group: '系统与治理', icon: '⚖', permissions: ['admin.audit.read'] },
  { id: 'audit', label: '审计日志', group: '系统与治理', icon: '≡', permissions: ['admin.audit.read'] },
] as const
export type AdminTab = typeof adminSections[number]['id']
export function visibleAdminSections(allowed: (permission: string) => boolean) {
  return adminSections.filter(item => !item.permissions.length || item.permissions.some(allowed))
}
