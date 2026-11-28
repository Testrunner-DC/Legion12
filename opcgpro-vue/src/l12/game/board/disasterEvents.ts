// B5 阶段 3（候选 6）：天灾揭示事件判定下沉。
export function isDisasterRevealEvent(event: { type: string }) { return event.type === 'disaster-reveal' }
