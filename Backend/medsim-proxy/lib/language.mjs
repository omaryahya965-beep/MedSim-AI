// Conversation language: detection of the student's language, explicit language commands, and the reply-language rule.
// Shared by the native voice path (language known only after the Realtime session has heard the audio) and typed turns.

const AR_LETTER = /[ء-يٱ-ۓۺ-ۿ]/;
const AR_WORD = /[ء-يٱ-ۓۺ-ۿ]+/g;
const LATIN_WORD = /[A-Za-z]{2,}/g;

/** 'ar' | 'en' | null — Arabic if a meaningful part of the words are Arabic (mixed "عندك chest pain؟" counts as Arabic). */
export function detectLang(text) {
  const t = String(text || '');
  const ar = (t.match(AR_WORD) || []).length, en = (t.match(LATIN_WORD) || []).length;
  if (ar === 0 && en === 0) return null;
  if (ar >= 2 || (ar >= 1 && ar >= en * 0.5)) return 'ar';
  return 'en';
}

/** Language of a reply by script (what will actually be spoken). */
export function scriptLang(text) {
  const t = String(text || '');
  const ar = (t.match(AR_WORD) || []).join('').length, en = (t.match(LATIN_WORD) || []).join('').length;
  if (!ar && !en) return null;
  return ar >= en * 0.6 ? 'ar' : 'en';
}

const n = s => String(s || '').toLowerCase().normalize('NFKC').replace(/[ً-ْـ]/g, '').replace(/[إأآ]/g, 'ا').replace(/ى/g, 'ي').replace(/ة/g, 'ه').replace(/[؟?!.,،]/g, ' ').replace(/\s+/g, ' ').trim();

// Arabic: verbs/phrases about speaking/switching/continuing + "Arabic"; "English" variants (انجليزي/انكليزي/إنجليزي/الإنجليزية).
const AR_ARABIC = /(عربي|العربي|بالعربي|العربيه|بالعربيه|عربيه)/;
const AR_ENGLISH = /(انجليزي|انكليزي|انقليزي|الانجليزي|بالانجليزي|بالانكليزي|الانجليزيه|بالانجليزيه|انجلش|انغلش|english)/;
const AR_VERB = /(احكي|احكو|احكوا|حكي|نحكي|تحكي|تحكيلي|تحكو|حول|حولي|حوّل|نحول|غير|غيري|نغير|كمل|كملي|نكمل|تكمل|بدي|بدنا|خلينا|خليني|ممكن|لو سمحت|رجع|ارجع|نرجع|رد|ردي|جاوب|جاوبني|اتكلم|تكلم|نتكلم|تتكلم|تحدث|نتحدث|يا ريت|من فضلك|صار|خلص)/;
const EN_SWITCH = /\b(speak|talk|answer|reply|respond|continue|switch|change|use|let'?s (?:speak|talk|go|continue|switch)|can you|could you|please)\b[^.?!]{0,40}\b(arabic|english)\b|\bin (arabic|english),? please\b|\b(switch|change) (?:the )?language to (arabic|english)\b|^(arabic|english),? please$|^(in )?(arabic|english)$/;

/**
 * Explicit language command → 'ar' | 'en' | null. Only short utterances whose purpose is the language itself
 * (so "how do you say chest pain in Arabic?" or a clinical question mentioning English is not a command).
 */
export function detectLanguageCommand(text) {
  const t = n(text); if (!t) return null;
  const words = t.split(' ').length;
  if (words > 12) return null;
  const hasAr = AR_ARABIC.test(t), hasEn = AR_ENGLISH.test(t);
  if ((hasAr || hasEn) && (AR_VERB.test(t) || words <= 3)) {
    if (hasAr && !hasEn) return 'ar';
    if (hasEn && !hasAr) return 'en';
    // "مش انجليزي، عربي" / "بدل الانجليزي احكي عربي" → the language after the last mention
    return t.lastIndexOf('عربي') > Math.max(t.lastIndexOf('انجليزي'), t.lastIndexOf('انكليزي'), t.lastIndexOf('english')) ? 'ar' : 'en';
  }
  const m = t.match(EN_SWITCH);
  if (m) { const lang = (m[2] || m[3] || m[4] || m[5] || m[6] || '').toLowerCase(); if (lang === 'arabic') return 'ar'; if (lang === 'english') return 'en'; }
  return null;
}

/** Reply language for a turn: explicit lock wins; otherwise the language the student just used; otherwise the conversation language. */
export function resolveReplyLang({ lock, heard, conversation, spoken }) {
  if (lock === 'ar' || lock === 'en') return lock;
  // The language the student SPOKE (identified from the audio before transcribing) wins over the transcript's script:
  // the Realtime model occasionally translates Arabic speech into an English transcript — that used to flip replies to English.
  if (spoken === 'arabic' || spoken === 'mixed') return 'ar';
  if (spoken === 'english') return detectLang(heard) === 'ar' ? 'ar' : 'en';
  return detectLang(heard) || (conversation === 'ar' ? 'ar' : 'en');
}

/** Prompt rule for the reply language. For voice (language unknown until heard) it is adaptive. */
export function replyLanguageRule(persona, { lock, known }) {
  const arStyle = persona === 'Patient'
    ? 'natural spoken PALESTINIAN colloquial Arabic, exactly as a 58-year-old man from Ramallah talks to a young doctor (e.g. "بلّش الوجع من شي خمسة وأربعين دقيقة، كنت طالع الدرج"). The Arabic fact wording given above is only a formal reference translation: say the same details in your own everyday Palestinian words, never in formal written Arabic'
    : 'clear, warm, correct spoken Arabic as a Palestinian hospital doctor talks to a medical student: simple Levantine colloquial in short sentences (standard terms like ECG, troponin, cath lab may stay in English). You are a WOMAN: use feminine forms for yourself (مبسوطة، متأكدة، بدي أسألك). Avoid formal written Arabic and rare words';
  if (lock === 'ar') return `Reply in ${arStyle}. The student has chosen Arabic for the whole conversation: ALWAYS reply in Arabic, even if they use English words or English sentences.`;
  if (lock === 'en') return 'Reply in natural spoken English. The student has chosen English for the whole conversation: always reply in English.';
  if (known === 'ar') return `Reply in ${arStyle}.`;
  if (known === 'en') return 'Reply in natural spoken English.';
  return `Reply in the SAME language the student used in this turn: if they spoke Arabic (any dialect, Palestinian colloquial, standard Arabic, or Arabic mixed with English medical terms), reply in ${arStyle}; if they spoke English, reply in natural spoken English.`;
}

export const LANGUAGE_ACK = {
  Patient: { ar: 'أكيد، خلينا نحكي عربي.', en: 'Sure, we can speak English.' },
  Instructor: { ar: 'تمام، بنكمّل بالعربي.', en: "Of course, let's continue in English." },
};
