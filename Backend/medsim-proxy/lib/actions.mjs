// Embodied actions: the AI may only PROPOSE one action from a fixed catalogue (structured, validated); the deterministic
// validator decides (authored scenario policy + clinical state); Unity executes only validated actions and reports the
// result back. No free-form code, no scene edits, and the character never claims an action that was not executed.

export const PATIENT_ACTIONS = ['none', 'sit_up', 'recline', 'raise_arm', 'extend_arm', 'lower_arm', 'turn_to_side', 'shift_on_bed',
  'deep_breath', 'breathe_normally', 'open_mouth', 'close_mouth', 'look_at_student', 'point_to_pain', 'hold_still', 'rest',
  'stand_up', 'walk', 'other_physical'];
export const DOCTOR_ACTIONS = ['none', 'come_to_student', 'stand_beside_student', 'step_back', 'approach_patient', 'wait',
  'return_to_observation', 'look_at_patient', 'look_at_student', 'demonstrate_instrument', 'other_physical'];
export const SIDES = ['left', 'right', 'both', 'unspecified', 'none'];
export const INSTRUMENTS = ['none', 'bp_cuff', 'pulse_oximeter', 'stethoscope', 'ecg'];
const NEEDS_SIDE = new Set(['raise_arm', 'extend_arm', 'turn_to_side']);

export function actionSchema(persona) {
  return { type: 'object', additionalProperties: false, required: ['type', 'side', 'instrument'], description: 'Physical action the student asked YOU to do now (none if they did not ask for one).',
    properties: { type: { type: 'string', enum: persona === 'Patient' ? PATIENT_ACTIONS : DOCTOR_ACTIONS },
      side: { type: 'string', enum: SIDES, description: 'Your own left/right for arms or turning; unspecified if the student did not say and it matters; none if not applicable.' },
      instrument: { type: 'string', enum: INSTRUMENTS } } };
}

export function actionPromptRule(persona) {
  return persona === 'Patient'
    ? `- PHYSICAL REQUESTS: if the student asks you to do something with your body (sit up / sit forward, lie back, raise, move, extend or relax an arm, bring your hand closer, turn onto a side, move on the bed, take a deep breath, breathe normally, open your mouth, look at them, show where it hurts, keep still, rest), set action.type (+ side for arms/turning, using YOUR left/right; "unspecified" if they did not say which arm). Different wording, dialect or language means the same action ("ارفع إيدك الشمال" = raise_arm left, "قرّب إيدك علي" = extend_arm). Reply with a SHORT natural acknowledgement in character ("حاضر", "طيب، هيك؟", "Sure.") — never say it is already done and never describe extra movements. Showing where it hurts («فرجيني وين الوجع», "show me where it hurts") = point_to_pain (you point to the middle of your chest). Use other_physical ONLY for a clearly physical request that matches nothing above. If they ask you to stand up, walk or get off the bed, set stand_up / walk (the simulation decides). A question about a symptom is NOT a physical request.`
    : `- PHYSICAL REQUESTS: if the student asks YOU to move or show something (come here, stand beside me, move back / give me space, wait, go back, show me how to use a device), set action.type. "Come and see/check the patient" («تعالي شوفي المريض») = approach_patient (walk to the bedside); look_at_patient is only turning to look without walking (and action.instrument for demonstrations: bp_cuff, pulse_oximeter, stethoscope or ecg). Reply with a SHORT natural acknowledgement ("أكيد، جاية", "Sure."), never claim it is done. A demonstration is only showing where the device goes and how to hold it — never reveal findings, results or the diagnosis.`;
}

const LINES = {
  denied_mobilise: { Patient: { en: "Sorry, doctor, I can't get up right now. The pain is too much.", ar: 'معلش يا دكتور، ما بقدر أقوم هلّق، الوجع كتير.' } },
  clarify_side: { Patient: { en: 'Which arm, doctor, the left or the right?', ar: 'أي إيد يا دكتور، اليمين ولا الشمال؟' } },
  clarify_other: { Patient: { en: 'Sorry doctor, what would you like me to do?', ar: 'معلش يا دكتور، شو بدك أعمل بالزبط؟' },
    Instructor: { en: 'What would you like me to do?', ar: 'شو بتحب أعمل بالزبط؟' } },
};
export function actionLine(key, persona, lang) { return LINES[key]?.[persona]?.[lang === 'ar' ? 'ar' : 'en'] || null; }

/**
 * Deterministic validation against the authored scenario policy (scenario.actionPolicy) and the current clinical state.
 * Returns { type, side, instrument, status: 'none'|'accepted'|'denied'|'clarify', reason, lineKey }.
 */
// Safety net ONLY when the AI could not place a clearly physical request: a few well-known phrasings → catalogue action.
const REMAP = { Patient: [[/(فرج|وري|ورج|show|point).{0,25}(وين|where|محل|مكان).{0,25}(وجع|بوجع|يوجع|hurt|pain|ألم)/i, 'point_to_pain'], [/(خذ|خود|take).{0,10}(نفس|breath)/i, 'deep_breath'], [/(افتح|open).{0,10}(ثم|تم|فم|mouth)/i, 'open_mouth']],
  Instructor: [[/(تعال|come).{0,20}(شوف|check|see|look).{0,20}(المريض|patient)/i, 'approach_patient']] };
export function validateAction(persona, a, { scenario, stateId, utterance = '' }) {
  let type = String(a?.type || 'none');
  if (type === 'other_physical') { const m = (REMAP[persona] || []).find(([re]) => re.test(utterance)); if (m) type = m[1]; } let side = String(a?.side || 'none'); const instrument = String(a?.instrument || 'none');
  const catalogue = persona === 'Patient' ? PATIENT_ACTIONS : DOCTOR_ACTIONS;
  if (!catalogue.includes(type) || type === 'none') return { type: 'none', side: 'none', instrument: 'none', status: 'none' };
  if (type === 'other_physical') return { type, side, instrument, status: 'clarify', reason: 'not_in_catalogue', lineKey: 'clarify_other' };
  const policy = scenario?.actionPolicy?.[persona === 'Patient' ? 'patient' : 'doctor'] || {};
  const denied = new Set(policy.alwaysDenied || []);
  const stateDenied = new Set((policy.deniedInStates || {})[stateId] || []);
  if (denied.has(type) || stateDenied.has(type)) return { type, side, instrument, status: 'denied', reason: denied.has(type) ? 'scenario_policy' : 'clinical_state:' + stateId, lineKey: policy.deniedLine?.[type] || 'denied_mobilise' };
  if (NEEDS_SIDE.has(type) && (side === 'unspecified' || side === 'none' || side === 'both')) {
    if (type === 'turn_to_side' || side === 'both') return { type, side, instrument, status: 'clarify', reason: 'side_needed', lineKey: 'clarify_side' };
    return { type, side, instrument, status: 'clarify', reason: 'side_needed', lineKey: 'clarify_side' };
  }
  if (type === 'lower_arm' && !['left', 'right', 'both'].includes(side)) side = 'both';
  if (type === 'demonstrate_instrument' && instrument === 'none') return { type, side, instrument, status: 'clarify', reason: 'instrument_needed', lineKey: 'clarify_other' };
  return { type, side, instrument, status: 'accepted' };
}

/** Short context lines for the AI about what really happened (Unity reports execution results). */
export function actionContext(sess) {
  const log = (sess.actionLog || []).slice(-4);
  return log.length ? 'Physical actions so far (what really happened in the simulation; never contradict this):\n' + log.map(x => `- ${x.persona === 'Patient' ? 'patient' : 'doctor'}: ${x.type}${x.side && x.side !== 'none' ? ' (' + x.side + ')' : ''} — ${x.status}`).join('\n') : '';
}
