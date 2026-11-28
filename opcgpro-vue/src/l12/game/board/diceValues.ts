// B5 阶段 3（候选 4）：掷骰结果解析从 GameBoard.vue 下沉为纯函数。
// 兼容两种服务端文案：单骰「结果为 N」与多骰「掷骰：a，b」。
export function diceValuesFromEventText(text: string): number[] {
  const result = text.match(/结果为\s*([1-6])/)?.[1]
  if (result) return [Number(result)]
  const rollText = text.split('掷骰：')[1] ?? ''
  return [...rollText.matchAll(/(?:^|\s)([1-6])(?=，|,|。|$)/g)].map(match => Number(match[1])).slice(-2)
}
