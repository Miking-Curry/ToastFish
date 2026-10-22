import { ref } from 'vue'
import { failure } from './api'
export const speechAvailable = typeof window !== 'undefined' && typeof window.speechSynthesis?.speak === 'function' && typeof window.SpeechSynthesisUtterance === 'function'
export const speaking = ref(false)
let current: SpeechSynthesisUtterance | null = null
export function stopSpeech() {
  current = null
  if (speechAvailable) window.speechSynthesis.cancel()
  speaking.value = false
}
export function pronounce(text: string, accent: 'en-GB' | 'en-US') {
  if (!speechAvailable) { failure.value = '此浏览器不支持朗读，请使用原生通知里的发音功能。'; return }
  if (!text.trim() || text.length > 300) { failure.value = '词条内容为空或过长，无法朗读。'; return }
  stopSpeech()
  const utterance = new SpeechSynthesisUtterance(text)
  current = utterance
  utterance.lang = accent
  utterance.rate = .85
  utterance.onstart = () => { if (current === utterance) speaking.value = true }
  utterance.onend = () => { if (current === utterance) { current = null; speaking.value = false } }
  utterance.onerror = event => {
    if (current !== utterance) return
    current = null
    speaking.value = false
    if (event.error !== 'canceled' && event.error !== 'interrupted') failure.value = '浏览器朗读不可用，请检查系统英语语音包，或使用通知中的发音。'
  }
  try {
    const voice = window.speechSynthesis.getVoices().find(v => v.lang.toLowerCase() === accent.toLowerCase())
    if (voice) utterance.voice = voice
    window.speechSynthesis.speak(utterance)
  } catch {
    current = null; speaking.value = false
    failure.value = '浏览器无法启动朗读，请使用原生通知里的发音。'
  }
}
