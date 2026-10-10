const label = (values: Record<string, string>, value?: string) => value ? values[value] || '未知状态' : '待定'

export const tournamentStatusText = (value: string) => label({
  registration: '赛前阶段', running: '进行中', completed: '已完成', canceled: '已取消',
}, value)

export const tournamentPhaseText = (value: string) => label({
  'registration-open': '报名开放', 'registration-closed': '报名关闭', 'pre-check-in': '赛前签到',
  running: '赛事进行中', 'result-confirmation': '成绩确认', completed: '已完成', canceled: '已取消',
}, value)

export const tournamentFormatText = (value: string) => label({
  single: '单败淘汰', swiss: '瑞士轮', 'swiss-cut': '瑞士轮加淘汰赛', league: '循环赛',
}, value)

export const tournamentRoundStatusText = (value: string) => label({
  pending: '待开始', checkin: '轮次签到', running: '进行中', completed: '已完成',
}, value)

export const tournamentMatchStatusText = (value: string) => label({
  waiting: '等待到齐', running: '进行中', completed: '已完成',
}, value)

export const tournamentResultText = (value?: string) => label({
  'player-a': 'A 方获胜', 'player-b': 'B 方获胜', draw: '平局',
  'no-show-a': 'A 方未到判负', 'no-show-b': 'B 方未到判负', bye: '轮空胜',
}, value)

export const tournamentViewerRoleText = (value: string) => label({
  organizer: '主办者', referee: '裁判', participant: '参赛者', viewer: '观众',
}, value)

export const tournamentDeckVisibilityText = (value: string) => label({
  always: '全程公开', after: '赛后公开', private: '仅工作人员可见',
}, value)

export const tournamentDisasterModeText = (value: string) => label({
  all: '全部天灾', random: '随机天灾', season: '赛季天灾', none: '不使用天灾',
}, value)

export const tournamentJudgeCategoryText = (value: string) => label({
  rules: '规则问题', technical: '技术问题', late: '迟到处理', result: '赛果争议',
}, value)

export const tournamentJudgeStatusText = (value: string) => label({
  pending: '等待受理', open: '等待受理', assigned: '已分派', investigating: '调查中', ruled: '已裁定',
  closed: '已关闭', rejected: '不予受理', appealed: '申诉复核中',
}, value)

export const tournamentJudgeUrgencyText = (value: string) => label({ normal: '普通', urgent: '紧急' }, value)
