export interface GeneratedPlayerReleaseSection {
  title: string
  items: string[]
}

export interface GeneratedPlayerReleaseEntry {
  date: string
  title: string
  version: string
  sections: GeneratedPlayerReleaseSection[]
}

// 普通开发与测试服构建保持为空。正式发布验证会在隔离构建目录中覆盖本文件，
// 内容只来自 release-ledger，并绑定“线上提交 -> 待发布提交”的精确区间。
export const generatedPlayerRelease: GeneratedPlayerReleaseEntry | null = null
