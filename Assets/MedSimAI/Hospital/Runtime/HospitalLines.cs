namespace MedSimAI.Hospital
{
    /// <summary>
    /// Authored hospital-guide lines (Dr. Sara Hassan), English and Palestinian Arabic. These are TEXT, not recordings: they
    /// are spoken at runtime through the existing speech system (live voice when the AI service is available, otherwise
    /// clearly labelled subtitles). No clinical facts, numbers or instructions are added here; the clinical and surgical
    /// content stays in each scenario's own authored data.
    /// </summary>
    public static class HospitalLines
    {
        public static string Get(string id, string lang)
        {
            bool ar = lang == "ar";
            switch (id)
            {
                case "welcome": return ar
                    ? "أهلاً وسهلاً دكتور. اليوم عنا حالتين للتدريب. بالغرفة الأولى في مريض بحاجة لفحص سريري، وبالغرفة الثانية عنا تدريب على محاكاة استبدال مفصل الركبة. وين بتحب نبلش؟"
                    : "Welcome, doctor. We have two training cases today. In Room 01 a patient needs a clinical examination, and in Room 02 there is a simulated knee replacement training. Where would you like to start?";
                case "welcome_short": return ar ? "تمام، منبلش من جديد. وين بتحب نروح: الغرفة الأولى ولا غرفة العمليات؟" : "All right, let's start again. Where would you like to go: Room 01 or the operating room?";
                case "explain_rooms": return ar
                    ? "بالغرفة الأولى بتفحص مريض عنده ألم بالصدر، وبتحكي معه وبتستخدم الأدوات. بالغرفة الثانية بتتدرب خطوة بخطوة على محاكاة تبديل مفصل الركبة، بأدوات افتراضية. المحتوى الجراحي مسودة تعليمية ومش معتمد سريرياً."
                    : "In Room 01 you examine a patient with chest pain, talk to him and use the instruments. In Room 02 you practise a step-by-step simulated knee replacement with virtual instruments. The surgical content is an educational draft and is not clinically validated.";
                case "where_to_go": return ar ? "فيك تختار. احكيلي الغرفة الأولى أو غرفة العمليات، أو اختار من اللوحة، وأنا بمشي معك." : "It's your choice. Tell me Room 01 or the operating room, or pick one on the sign, and I'll walk you there.";
                case "where_next_after_clinical": return ar ? "خلصنا الفحص بالغرفة الأولى. إذا حابب، فينا نروح هلأ على غرفة العمليات." : "We've finished the examination in Room 01. If you like, we can go to the operating room now.";
                case "where_next_after_surgical": return ar ? "خلصنا تدريب العملية. إذا حابب، فينا نروح على الغرفة الأولى لفحص المريض." : "We've finished the surgical training. If you like, we can go to Room 01 to examine the patient.";
                case "both_done": return ar ? "خلصت الحالتين اليوم. بتقدر تعيد أي وحدة منهم وقت ما بدك." : "You've completed both cases today. You can repeat either of them whenever you like.";
                case "clarify_room": return ar ? "أي غرفة بتقصد: الغرفة الأولى ولا غرفة العمليات؟" : "Which room do you mean: Room 01 or the operating room?";
                case "follow_room01": return ar ? "تمام، تعال معي على الغرفة الأولى." : "Sure, follow me to Room 01.";
                case "follow_room02": return ar ? "تمام، تعال معي على غرفة العمليات." : "Sure, follow me to the operating room.";
                case "follow_corridor": return ar ? "تمام، خلينا نطلع عالممر." : "All right, let's go back to the corridor.";
                case "already_room01": return ar ? "إحنا بالغرفة الأولى هلأ." : "We're in Room 01 now.";
                case "already_room02": return ar ? "إحنا بغرفة العمليات هلأ." : "We're in the operating room now.";
                case "already_corridor": return ar ? "إحنا بالممر هلأ." : "We're in the corridor now.";
                case "waiting": return ar ? "أنا مستنيتك هون، خذ وقتك." : "I'll wait for you here, take your time.";
                case "resume": return ar ? "يلا نكمل." : "Let's carry on.";
                case "wait_ack": return ar ? "أكيد، مستنية." : "Of course, I'll wait.";
                case "walk_with_ack": return ar ? "أكيد، أنا معك." : "Of course, I'm with you.";
                // Short on purpose: the surgeon's own welcome follows inside (no double introduction at the door).
                case "arrived_room02": return ar ? "وصلنا. الباب بيفتح لحاله، تفضّل." : "Here we are. The doors open by themselves. After you.";
                // Returning to a room already visited in this hospital visit: no second full introduction.
                case "room01_again": return ar ? "رجعنا عند السيد رييس. منبلش الحالة من جديد، وأنا معك إذا احتجت إشي." : "We're back with Mr. Reyes. The case starts again from the beginning, and I'm here if you need me.";
                case "room02_again": return ar ? "رجعنا لغرفة العمليات. منبلش من أول خطوة، وأنا وسامي معك." : "Back in the operating room. We start again from the first step; Sami and I are with you.";
                case "back_corridor": return ar ? "رجعنا عالممر. وين بتحب نروح؟" : "We're back in the corridor. Where would you like to go?";
                case "left_unfinished": return ar ? "وقفنا الحالة قبل ما تخلص. فيك ترجع تبلّشها من جديد وقت ما بدك." : "We stopped that case before it was finished. You can start it again whenever you like.";
                case "no_path": return ar ? "ما قدرت أوصل من هون. اتبع اللافتات وأنا بلحقك." : "I can't get there from here. Follow the signs and I'll catch up with you.";
                case "skip_ack": return ar ? "تمام. اختار الغرفة اللي بدك ياها." : "Fine. Choose the room you want.";
                default: return "";
            }
        }
    }
}
