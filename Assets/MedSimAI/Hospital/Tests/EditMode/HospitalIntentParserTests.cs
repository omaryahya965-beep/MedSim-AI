using NUnit.Framework;

namespace MedSimAI.Hospital.Tests
{
    /// <summary>The navigation requests from the brief (Palestinian Arabic, English, mixed) and what must NOT be taken as navigation.</summary>
    public class HospitalIntentParserTests
    {
        static HospitalIntent P(string s, HospitalRoomId where = HospitalRoomId.Corridor) => HospitalIntentParser.Parse(s, where);

        [TestCase("وين غرفة العمليات؟", HospitalRoomId.Room02)]
        [TestCase("خلينا نروح عند المريض.", HospitalRoomId.Room01)]
        [TestCase("بدي أتعلم العملية.", HospitalRoomId.Room02)]
        [TestCase("فرجيني غرفة العمليات.", HospitalRoomId.Room02)]
        [TestCase("رجعني عالغرفة الأولى.", HospitalRoomId.Room01)]
        [TestCase("Where is the operating room?", HospitalRoomId.Room02)]
        [TestCase("Take me to the patient please", HospitalRoomId.Room01)]
        [TestCase("I'd like to learn the knee surgery", HospitalRoomId.Room02)]
        [TestCase("Let's go back to room one", HospitalRoomId.Room01)]
        [TestCase("خليني أروح على the operating room", HospitalRoomId.Room02)]
        [TestCase("take me لغرفة العمليات", HospitalRoomId.Room02)]
        [TestCase("الغرفة التانية", HospitalRoomId.Room02)]
        [TestCase("2", HospitalRoomId.Room02)]
        [TestCase("خلينا نطلع عالممر", HospitalRoomId.Corridor)]
        public void Corridor_RoomRequests(string text, HospitalRoomId room)
        {
            var i = P(text);
            Assert.AreEqual(HospitalIntentKind.GoToRoom, i.kind, text + " → " + i);
            Assert.AreEqual(room, i.room, text + " → " + i);
        }

        [TestCase("امشي معي.", HospitalIntentKind.WalkWithMe)]
        [TestCase("Walk with me", HospitalIntentKind.WalkWithMe)]
        [TestCase("استني شوي.", HospitalIntentKind.Wait)]
        [TestCase("Wait a moment", HospitalIntentKind.Wait)]
        [TestCase("وين لازم أروح؟", HospitalIntentKind.WhereToGo)]
        [TestCase("Where should I go?", HospitalIntentKind.WhereToGo)]
        [TestCase("بدي أبدأ من جديد.", HospitalIntentKind.Restart)]
        [TestCase("I want to start over", HospitalIntentKind.Restart)]
        [TestCase("شو في عنا اليوم؟", HospitalIntentKind.ExplainRooms)]
        [TestCase("room 1 or room 2?", HospitalIntentKind.Clarify)]
        public void Corridor_OtherRequests(string text, HospitalIntentKind kind)
        {
            var i = P(text);
            Assert.AreEqual(kind, i.kind, text + " → " + i);
        }

        [TestCase("When did the pain start?")]
        [TestCase("Does the pain spread anywhere?")]
        [TestCase("Have you had any surgery before?")]
        [TestCase("وين عملت العملية قبل؟")]
        [TestCase("بدي أفحص صدرك")]
        [TestCase("Is the pain in your chest worse when you breathe?")]
        public void InsideRoom01_ClinicalTalkIsNotNavigation(string text)
        {
            var i = P(text, HospitalRoomId.Room01);
            Assert.IsFalse(i.kind == HospitalIntentKind.GoToRoom && i.room != HospitalRoomId.Room01, text + " → " + i);
        }

        // Talking to Dr. Sara Hassan uses FEMININE imperatives (خديني, رجعيني, وصليني). Inside a room a movement verb is required,
        // so these must be in the verb list (found by the exe voice walkthrough: the brief's own example was ignored in Room 01).
        [TestCase("خديني على غرفة العمليات", HospitalRoomId.Room01, HospitalRoomId.Room02)]
        [TestCase("دكتورة خديني على غرفة العمليات لو سمحتي", HospitalRoomId.Room01, HospitalRoomId.Room02)]
        [TestCase("رجعيني عالممر", HospitalRoomId.Room02, HospitalRoomId.Corridor)]
        [TestCase("وصليني على الغرفة الأولى", HospitalRoomId.Room02, HospitalRoomId.Room01)]
        [TestCase("خديني على غرفة الفحص", HospitalRoomId.Room02, HospitalRoomId.Room01)]
        public void InsideRooms_FeminineImperativesToTheDoctor(string text, HospitalRoomId where, HospitalRoomId to)
        {
            var i = P(text, where);
            Assert.AreEqual(HospitalIntentKind.GoToRoom, i.kind, text + " → " + i);
            Assert.AreEqual(to, i.room, text + " → " + i);
        }

        [Test]
        public void InsideRooms_ExplicitRequestsStillWork()
        {
            Assert.AreEqual(HospitalRoomId.Room02, P("take me to the operating room", HospitalRoomId.Room01).room);
            Assert.AreEqual(HospitalRoomId.Room01, P("رجعني عالغرفة الأولى", HospitalRoomId.Room02).room);
            Assert.AreEqual(HospitalRoomId.Corridor, P("رجعني عالممر", HospitalRoomId.Room02).room);
            // In the operating room "the patient" is the surgical patient, not Room 01.
            Assert.AreEqual(HospitalIntentKind.None, P("خلينا نروح عند المريض", HospitalRoomId.Room02).kind);
        }

        [Test]
        public void Normalize_UnifiesArabicLetterForms()
        {
            Assert.AreEqual(HospitalIntentParser.Normalize("الغرفة الأولى"), HospitalIntentParser.Normalize("الغرفه الاولي"));
            Assert.AreEqual(HospitalIntentParser.Normalize("إستنّى"), HospitalIntentParser.Normalize("استني"));
        }

        [Test]
        public void EmptyOrUnrelated_IsNone()
        {
            Assert.AreEqual(HospitalIntentKind.None, P("").kind);
            Assert.AreEqual(HospitalIntentKind.None, P("thank you").kind);
            Assert.AreEqual(HospitalIntentKind.None, P("شكراً كتير").kind);
        }

        [Test]
        public void Lines_ExistInBothLanguages()
        {
            foreach (var id in new[] { "welcome", "explain_rooms", "where_to_go", "follow_room01", "follow_room02", "waiting", "arrived_room02", "no_path", "left_unfinished" })
            {
                Assert.IsNotEmpty(HospitalLines.Get(id, "en"), id);
                Assert.IsNotEmpty(HospitalLines.Get(id, "ar"), id);
            }
            StringAssert.Contains("وين بتحب نبلش", HospitalLines.Get("welcome", "ar"));
        }
    }
}
