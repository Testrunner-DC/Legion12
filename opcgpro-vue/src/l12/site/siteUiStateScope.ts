/** Ordinary site controls share states; immersive and logical-canvas controls do not. */
export function usesSiteUiStates(meta: Readonly<Record<string, unknown>>) {
  return meta.immersive !== true && meta.landscapeCanvas !== true
}
