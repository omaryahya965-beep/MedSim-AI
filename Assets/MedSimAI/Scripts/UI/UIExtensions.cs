namespace MedSimAI.UI
{
    public static class UIExtensions
    {
        /// <summary>Kotlin-style "also" for concise UI construction.</summary>
        public static T Also<T>(this T self, System.Action<T> f) { f(self); return self; }
    }
}
