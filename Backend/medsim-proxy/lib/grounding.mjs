// Deterministic medical-grounding checks for AI-generated character speech. Pure functions, no network.
// Principle: generated text may vary in wording, but every number and every clinical term it contains must already
// exist in an "allowed corpus" built from authoritative scenario content (plus the student's own words). Anything else
// is rejected and replaced with approved scenario text. This does not rely on model self-reported confidence.

const EN_NUM = { zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10, eleven: 11, twelve: 12, thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17, eighteen: 18, nineteen: 19, twenty: 20, thirty: 30, forty: 40, fifty: 50, sixty: 60, seventy: 70, eighty: 80, ninety: 90, hundred: 100 };
const AR_NUM = { 'واحد': 1, 'اثنين': 2, 'ثنتين': 2, 'تنين': 2, 'ثلاث': 3, 'ثلاثة': 3, 'تلات': 3, 'اربع': 4, 'أربع': 4, 'أربعة': 4, 'خمس': 5, 'خمسة': 5, 'ست': 6, 'ستة': 6, 'سبع': 7, 'سبعة': 7, 'ثمان': 8, 'ثمانية': 8, 'تمانية': 8, 'تمنية': 8, 'تسع': 9, 'تسعة': 9, 'عشر': 10, 'عشرة': 10, 'عشرين': 20, 'ثلاثين': 30, 'تلاتين': 30, 'أربعين': 40, 'اربعين': 40, 'خمسين': 50, 'ستين': 60, 'مية': 100, 'مئة': 100 };

// Clinical lexicon: terms whose presence in generated speech must be justified by the allowed corpus.
// Deliberately broad (symptoms, anatomy, conditions, drugs, tests, procedures) in English and Arabic.
export const CLINICAL_LEXICON = [
  // symptoms / signs
  'pain', 'ache', 'crushing', 'pressure', 'tight', 'heavy', 'burning', 'stabbing', 'sharp', 'sweat', 'clammy', 'nause', 'vomit', 'sick', 'dizzy', 'faint', 'breathless', 'short of breath', 'palpitation', 'cough', 'fever', 'rash', 'swelling', 'numb', 'tingl', 'headache', 'syncope', 'collapse', 'tired',
  // anatomy
  'chest', 'arm', 'jaw', 'neck', 'shoulder', 'stomach', 'abdomen', 'leg', 'heart', 'lung', 'throat',
  // conditions
  'blood pressure', 'hypertension', 'cholesterol', 'diabetes', 'asthma', 'copd', 'angina', 'stroke', 'heart attack', 'infarction', 'stemi', 'nstemi', 'coronary', 'ischaemi', 'ischemi', 'embolism', 'dissection', 'pericarditis', 'reflux', 'cancer', 'kidney', 'arrhythmi', 'fibrillation', 'heart failure',
  // drugs
  'amlodipine', 'atorvastatin', 'statin', 'aspirin', 'clopidogrel', 'ticagrelor', 'heparin', 'morphine', 'nitro', 'gtn', 'penicillin', 'amoxicillin', 'insulin', 'metformin', 'warfarin', 'ibuprofen', 'paracetamol', 'oxygen', 'medication', 'tablet', 'pill', 'inhaler',
  // habits / history
  'smok', 'cigarette', 'alcohol', 'beer', 'father', 'mother', 'brother', 'sister', 'allerg',
  // tests / procedures / results
  'ecg', 'ekg', 'troponin', 'x-ray', 'xray', 'echo', 'glucose', 'sugar', 'scan', 'ct', 'mri', 'blood test', 'st elevation', 'st depression', 'saturation', 'spo2', 'pulse', 'heart rate', 'murmur', 'crackles', 'wheeze', 'cath', 'pci', 'stent', 'angioplasty', 'surgery', 'operation', 'thromboly',
  // Arabic
  'ألم', 'وجع', 'صدر', 'ذراع', 'إيد', 'فك', 'رقبة', 'ظهر', 'تعرق', 'عرق', 'غثيان', 'دوخة', 'ضيق', 'قلب', 'سكري', 'كوليسترول', 'دهون', 'حساسية', 'بنسلين', 'دخان', 'تدخين', 'سجائر', 'سيجارة', 'جلطة', 'احتشاء', 'ذبحة', 'أملوديبين', 'أتورفاستاتين', 'أسبرين', 'دواء', 'حبوب', 'تخطيط', 'تروبونين', 'أبوي', 'أبوه', 'أبي', 'والدي'
];

const DIAGNOSIS = ['stemi', 'nstemi', 'myocardial infarction', 'heart attack', 'acute coronary', 'angina', 'pericarditis', 'dissection', 'embolism', 'احتشاء', 'جلطة قلبية', 'نوبة قلبية', 'جلطة بالقلب', 'ذبحة'];
const DOSING = [/\b\d+(\.\d+)?\s?(mg|mcg|µg|ml|units?|iu|g)\b/i, /\bdose\b/i, /\bdosage\b/i, /\bprescrib/i, /جرعة/];
const META = [/\bsimulation\b/i, /\bscenario\b/i, /\brubric\b/i, /\bgrad(e|ing)\b/i, /\bscore\b/i, /\bai\b/i, /language model/i, /\bprompt\b/i, /محاكاة/, /سيناريو/];

const UNIT = /^(minute|hour|day|week|month|year|time|tablet|pill|cigarette|pack|drink|beer|out|of|دقيق|ساع|يوم|اسبوع|شهر|سن|مر|حب|سيجار|علب)/;
export const norm = s => String(s || '').toLowerCase().normalize('NFKC').replace(/[ً-ْـ]/g, '').replace(/[إأآ]/g, 'ا').replace(/ة\b/g, 'ه').replace(/’/g, "'");

/** All numeric values mentioned (digits, Arabic-Indic digits, English and Arabic number words incl. "forty-five"). */
export function extractNumbers(text) {
  const t = norm(text).replace(/[٠-٩]/g, d => String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)));
  const out = new Set();
  for (const m of t.matchAll(/\d+(?:\.\d+)?/g)) out.add(Number(m[0]));
  const words = t.split(/[^a-z؀-ۿ\-]+/).filter(Boolean);
  for (let i = 0; i < words.length; i++) {
    const w = words[i];
    const parts = w.split('-');
    if (parts.length === 2 && EN_NUM[parts[0]] >= 20 && EN_NUM[parts[1]] < 10) { out.add(EN_NUM[parts[0]] + EN_NUM[parts[1]]); continue; }
    // "one" ("no one", "this one") and Arabic "واحد"/"ست" are usually not quantities: count them only before a unit word.
    if ((w === 'one' || w === 'واحد' || w === 'ست') && !UNIT.test(words[i + 1] || '')) continue;
    if (w in EN_NUM) {
      let v = EN_NUM[w];
      if (v >= 20 && v < 100 && EN_NUM[words[i + 1]] > 0 && EN_NUM[words[i + 1]] < 10) { v += EN_NUM[words[i + 1]]; i++; }
      out.add(v); continue;
    }
    const aw = w.replace(/^و/, '');
    const arKey = Object.keys(AR_NUM).find(k => norm(k) === aw);
    if (arKey) out.add(AR_NUM[arKey]);
  }
  // Arabic "five and forty" = 45
  const arr = [...t.matchAll(/(\S+)\s+و\s?(\S+)/g)];
  for (const m of arr) {
    const a = AR_NUM[Object.keys(AR_NUM).find(k => norm(k) === m[1])], b = AR_NUM[Object.keys(AR_NUM).find(k => norm(k) === m[2])];
    if (a && b && a < 10 && b >= 20) out.add(a + b);
  }
  return out;
}

function termIn(term, text) {
  const t = norm(term);
  if (/^[a-z0-9]{1,3}$/.test(t)) return new RegExp(`(^|[^a-z0-9])${t}([^a-z0-9]|$)`).test(text);
  return text.includes(t);
}

/**
 * Validates generated speech against an allowed corpus.
 * @returns {{ok:boolean, reason?:string, detail?:string}}
 */
export function validateGrounded(reply, { corpus = [], studentText = '', allowDiagnosis = false, maxWords = 70, allowNumbers = true, allowMeta = false } = {}) {
  const r = String(reply || '').trim();
  if (!r) return { ok: false, reason: 'empty' };
  const words = r.split(/\s+/).length;
  if (words > maxWords) return { ok: false, reason: 'too_long', detail: String(words) };
  const allowed = norm([...corpus, studentText].join(' \n '));
  const rn = norm(r);
  for (const re of DOSING) if (re.test(r)) return { ok: false, reason: 'dosing' };
  if (!allowMeta) for (const re of META) if (re.test(r)) return { ok: false, reason: 'meta_reference' };
  if (/https?:|doi:|et al/i.test(r)) return { ok: false, reason: 'citation' };
  if (!allowDiagnosis) for (const d of DIAGNOSIS) if (termIn(d, rn) && !termIn(d, norm(corpus.join(' ')))) return { ok: false, reason: 'diagnosis_disclosure', detail: d };
  const nums = extractNumbers(r);
  if (nums.size) {
    if (!allowNumbers) return { ok: false, reason: 'numbers_not_allowed' };
    const allowedNums = extractNumbers(allowed);
    for (const n of nums) if (!allowedNums.has(n)) return { ok: false, reason: 'ungrounded_number', detail: String(n) };
  }
  for (const term of CLINICAL_LEXICON) if (termIn(term, rn) && !termIn(term, allowed)) return { ok: false, reason: 'ungrounded_clinical_term', detail: term };
  return { ok: true };
}

/** Deterministic concept detection used for reasoning assessment (mirrors the C# ConceptMatcher). */
export function matchConcepts(text, concepts = []) {
  const t = norm(text);
  return concepts.filter(c => (c.anyOf || []).some(k => termIn(k, t)) && (!(c.alsoAnyOf || []).length || c.alsoAnyOf.some(k => termIn(k, t)))).map(c => c.id);
}
