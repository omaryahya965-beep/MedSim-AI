using System.Linq;
using System.Text;

namespace MedSimAI.Hospital
{
    public enum HospitalRoomId { None, Corridor, Room01, Room02 }

    public enum HospitalIntentKind
    {
        None,
        GoToRoom,       // "وين غرفة العمليات؟", "take me to the operating room", "رجعني عالغرفة الأولى"
        WhereToGo,      // "وين لازم أروح؟", "where should I go?"
        ExplainRooms,   // "شو في عنا؟", "what can I train on?"
        Wait,           // "استني شوي", "wait"
        WalkWithMe,     // "امشي معي", "come with me"
        Restart,        // "بدي أبدأ من جديد", "start over"
        Skip,           // "خلص، يلا نبلش", "skip"
        Clarify         // both rooms named: ask which one
    }

    public struct HospitalIntent
    {
        public HospitalIntentKind kind;
        public HospitalRoomId room;
        public string cue;   // the phrase that matched (logs and tests)
        public override string ToString() => kind + (room != HospitalRoomId.None ? "(" + room + ")" : "") + (string.IsNullOrEmpty(cue) ? "" : " ← '" + cue + "'");
    }

    /// <summary>
    /// Deterministic understanding of hospital navigation requests in English, Arabic (including Palestinian colloquial) and
    /// mixed speech. Used on the transcript of every student utterance (native Realtime "heard" text, Windows dictation, or
    /// typed text) before the clinical / surgical conversation sees it. It never decides anything clinical, and an utterance
    /// it does not recognise is passed on unchanged. Engine-free, so it is unit-tested without Unity.
    /// </summary>
    public static class HospitalIntentParser
    {
        static readonly string[] Room02 =
        {
            "غرفه العمليات", "غرفه عمليات", "العمليات", "عمليات", "العمليه", "عمليه", "الجراحه", "جراحه", "جراحي", "الركبه", "ركبه", "المفصل",
            "الغرفه الثانيه", "الغرفه التانيه", "الغرفه رقم 2", "غرفه 2", "غرفه اتنين", "غرفه اثنين", "غرفه ٢",
            "operating room", "operating theatre", "operating theater", "theatre", "surgery", "surgical", "operation", "knee", "or room", "room 2", "room two", "second room", "room 02"
        };
        static readonly string[] Room01 =
        {
            "الغرفه الاولي", "اول غرفه", "غرفه الفحص", "الفحص السريري", "الفحص", "المريض", "مريض", "الصدر", "ألم الصدر", "الطوارئ", "الطوارء",
            "الغرفه رقم 1", "غرفه 1", "غرفه واحد", "غرفه ١",
            "examination", "exam room", "clinical", "patient", "chest", "emergency", "room 1", "room one", "first room", "room 01"
        };
        // Inside a room only unambiguous room names count (case talk mentions 'operation', 'chest', 'patient'...).
        static readonly string[] Room02Strong = { "غرفه العمليات", "غرفه عمليات", "الغرفه الثانيه", "الغرفه التانيه", "غرفه 2", "غرفه اتنين", "غرفه اثنين", "غرفه ٢", "الغرفه رقم 2", "operating room", "operating theatre", "operating theater", "room 2", "room two", "second room", "room 02" };
        static readonly string[] Room01Strong = { "الغرفه الاولي", "اول غرفه", "غرفه الفحص", "غرفه 1", "غرفه واحد", "غرفه ١", "الغرفه رقم 1", "exam room", "examination room", "room 1", "room one", "first room", "room 01" };
        static readonly string[] Corridor = { "الممر", "الكوريدور", "الاستقبال", "برا", "لبرا", "corridor", "hallway", "reception", "lobby", "outside", "the hall" };
        static readonly string[] Wait = { "استني", "استنا", "استنى", "لحظه", "ثانيه", "دقيقه", "wait", "hold on", "one moment", "just a moment", "stop here", "stay here" };
        static readonly string[] WalkWithMe = { "امشي معي", "امشي معاي", "تعال معي", "تعالي معي", "خليك معي", "خليكي معي", "تعالي معاي", "come with me", "walk with me", "stay with me", "follow me" };
        static readonly string[] Restart = { "من جديد", "من الاول", "من الأول", "اعيد", "نعيد", "restart", "start over", "start again", "reset", "begin again" };
        static readonly string[] WhereToGo = { "وين لازم اروح", "وين اروح", "وين بدي اروح", "لوين اروح", "شو لازم اعمل", "شو بعمل", "وين نروح", "where should i go", "where do i go", "where now", "what should i do", "what now", "which room" };
        static readonly string[] Explain = { "شو في", "شو عنا", "شو عندكم", "شو الغرف", "شو الحالات", "احكيلي عن", "what rooms", "what is there", "what can i do", "what can i train", "tell me about", "explain the rooms", "what are the rooms" };
        // Words that make a room mention a request to GO there (needed inside a room, where 'surgery' or 'chest' are just case talk).
        static readonly string[] Move =
        {
            "روح", "نروح", "اروح", "تروح", "خذني", "خدني", "وديني", "ودينا", "رجعني", "رجعنا",
            // feminine imperatives (the student is talking to Dr. Sara Hassan): خديني / رجعيني / وصليني …
            "خديني", "خوديني", "اخديني", "رجعيني", "وصليني", "وصلينا", "دليني", "دلينا", "خدينا", "خذيني", "رجعينا", "ارجع", "نرجع", "وين", "فرجيني", "وريني", "خلينا", "يلا",
            "اطلع", "نطلع", "امشي", "تعال", "نطلع ع", "اتعلم",
            "go ", "go to", "take me", "bring me", "show me", "back to", "return", "where is", "where's", "let's", "lets", "head to", "lead me", "walk me", "move to", "leave", "learn"
        };
        static readonly string[] Skip = { "تخطى", "تخطي", "يلا نبلش", "خلص يلا", "skip", "let's start", "lets start" };

        /// <summary>Lower-case, Arabic letter variants unified (أإآ→ا, ة→ه, ى→ي), diacritics, tatweel and punctuation removed.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch0 in s.ToLowerInvariant())
            {
                var ch = ch0;
                if (ch >= 'ً' && ch <= 'ٟ' || ch == 'ٰ' || ch == 'ـ') continue;   // tashkeel, tatweel
                if (ch == 'أ' || ch == 'إ' || ch == 'آ') ch = 'ا';
                else if (ch == 'ة') ch = 'ه';
                else if (ch == 'ى') ch = 'ي';
                else if (ch == '٠' || ch == '۰') ch = '0';
                if (char.IsPunctuation(ch) || ch == '؟' || ch == '،' || ch == '«' || ch == '»') { sb.Append(' '); continue; }
                sb.Append(ch);
            }
            var t = sb.ToString();
            // "ع" prefix ("عالغرفه" = على الغرفة) and "لل" contractions: also match with a separating space
            t = t.Replace("عال", "ع ال").Replace("عل ", "على ");
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return " " + t.Trim() + " ";
        }

        static string Any(string norm, string[] cues)
        {
            foreach (var c in cues)
            {
                var n = Normalize(c).Trim();
                if (n.Length > 0 && norm.Contains(n)) return c;
            }
            return null;
        }

        /// <summary>
        /// Parses one utterance. currentRoom gives context: inside the operating room "the patient" is the surgical patient,
        /// so it does not mean Room 01 there.
        /// </summary>
        public static HospitalIntent Parse(string text, HospitalRoomId currentRoom = HospitalRoomId.Corridor)
        {
            var n = Normalize(text);
            if (n.Trim().Length == 0) return default;
            string cue;

            if ((cue = Any(n, Restart)) != null) return new HospitalIntent { kind = HospitalIntentKind.Restart, room = RoomOf(n, currentRoom, out _), cue = cue };
            if ((cue = Any(n, WalkWithMe)) != null) return new HospitalIntent { kind = HospitalIntentKind.WalkWithMe, cue = cue };

            var room = RoomOf(n, currentRoom, out var roomCue);
            if (room == HospitalRoomId.None && roomCue == "both") return new HospitalIntent { kind = HospitalIntentKind.Clarify, cue = "both rooms" };
            if ((cue = Any(n, Wait)) != null && room == HospitalRoomId.None) return new HospitalIntent { kind = HospitalIntentKind.Wait, cue = cue };
            // Inside a room only an explicit request moves us ("have you had surgery?" is a clinical question, not navigation).
            bool asksToGo = currentRoom == HospitalRoomId.Corridor || Any(n, Move) != null || n.Trim().Split(' ').Length <= 3;
            if (room != HospitalRoomId.None && asksToGo) return new HospitalIntent { kind = HospitalIntentKind.GoToRoom, room = room, cue = roomCue };
            if (room != HospitalRoomId.None) return default;
            if ((cue = Any(n, WhereToGo)) != null) return new HospitalIntent { kind = HospitalIntentKind.WhereToGo, cue = cue };
            if ((cue = Any(n, Explain)) != null) return new HospitalIntent { kind = HospitalIntentKind.ExplainRooms, cue = cue };
            if ((cue = Any(n, Skip)) != null) return new HospitalIntent { kind = HospitalIntentKind.Skip, cue = cue };
            var bare = n.Trim();
            if (bare == "1" || bare == "١" || bare == "واحد" || bare == "one") return new HospitalIntent { kind = HospitalIntentKind.GoToRoom, room = HospitalRoomId.Room01, cue = bare };
            if (bare == "2" || bare == "٢" || bare == "اتنين" || bare == "two") return new HospitalIntent { kind = HospitalIntentKind.GoToRoom, room = HospitalRoomId.Room02, cue = bare };
            return default;
        }

        static HospitalRoomId RoomOf(string n, HospitalRoomId currentRoom, out string cue)
        {
            bool strict = currentRoom != HospitalRoomId.Corridor;
            var c2 = Any(n, strict ? Room02Strong : Room02);
            var c1 = Any(n, strict ? Room01Strong : Room01);
            var cc = Any(n, Corridor);
            // "operation"/"knee" words in a sentence that also names Room 01 explicitly: prefer the explicit room number
            if (c1 != null && c2 != null)
            {
                bool explicit1 = n.Contains(" الاولي ") || n.Contains("room 1") || n.Contains("room one") || n.Contains("first room") || n.Contains("غرفه 1");
                bool explicit2 = n.Contains("الثانيه") || n.Contains("التانيه") || n.Contains("room 2") || n.Contains("room two") || n.Contains("second room") || n.Contains("غرفه 2");
                if (explicit1 && !explicit2) { cue = c1; return HospitalRoomId.Room01; }
                if (explicit2 && !explicit1) { cue = c2; return HospitalRoomId.Room02; }
                cue = "both"; return HospitalRoomId.None;
            }
            if (c2 != null) { cue = c2; return HospitalRoomId.Room02; }
            if (c1 != null) { cue = c1; return HospitalRoomId.Room01; }
            if (cc != null) { cue = cc; return HospitalRoomId.Corridor; }
            cue = null; return HospitalRoomId.None;
        }
    }
}
