// Minimal NUnit-compatible shim so MedSim's pure-C# EditMode tests can compile and run with plain `dotnet run`,
// without Unity and without NuGet access. Only the API surface used by the project's tests is implemented.
// Inside Unity the real nunit.framework.dll is used; this file is NOT under Assets/ and Unity never sees it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class OneTimeSetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public class IgnoreAttribute : Attribute { public IgnoreAttribute(string reason = "") { } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public class CategoryAttribute : Attribute { public CategoryAttribute(string name = "") { } }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseAttribute : Attribute { public object[] Args; public TestCaseAttribute(params object[] args) { Args = args ?? new object[] { null }; } }

    public class AssertionException : Exception { public AssertionException(string m) : base(m) { } }

    public static class Assert
    {
        static void Fail_(string what, string msg) => throw new AssertionException(string.IsNullOrEmpty(msg) ? what : what + " — " + msg);
        public static void Fail(string msg = "") => Fail_("Assert.Fail", msg);
        public static void Pass(string msg = "") { }
        public static void IsTrue(bool c, string msg = "", params object[] a) { if (!c) Fail_("Expected true", Fmt(msg, a)); }
        public static void True(bool c, string msg = "") => IsTrue(c, msg);
        public static void IsFalse(bool c, string msg = "", params object[] a) { if (c) Fail_("Expected false", Fmt(msg, a)); }
        public static void False(bool c, string msg = "") => IsFalse(c, msg);
        public static void IsNull(object o, string msg = "") { if (o != null) Fail_($"Expected null but was <{o}>", msg); }
        public static void Null(object o, string msg = "") => IsNull(o, msg);
        public static void IsNotNull(object o, string msg = "") { if (o == null) Fail_("Expected not null", msg); }
        public static void NotNull(object o, string msg = "") => IsNotNull(o, msg);
        public static void IsEmpty(IEnumerable e, string msg = "") { if (e is string s ? s.Length > 0 : e.Cast<object>().Any()) Fail_("Expected empty", msg); }
        public static void IsNotEmpty(IEnumerable e, string msg = "") { if (e is string s ? s.Length == 0 : !e.Cast<object>().Any()) Fail_("Expected not empty", msg); }
        public static void Contains(object item, ICollection c, string msg = "") { if (!c.Cast<object>().Any(x => Equals(x, item))) Fail_($"Expected collection to contain <{item}>", msg); }
        public static void AreEqual(double e, double a, double delta, string msg = "") { if (Math.Abs(e - a) > delta) Fail_($"Expected {e} ± {delta} but was {a}", msg); }
        public static void AreEqual(object e, object a, string msg = "", params object[] args)
        {
            if (Same(e, a)) return;
            Fail_($"Expected <{e ?? "null"}> but was <{a ?? "null"}>", Fmt(msg, args));
        }
        public static void AreNotEqual(object e, object a, string msg = "") { if (Same(e, a)) Fail_($"Expected not <{e}>", msg); }
        public static void AreSame(object e, object a, string msg = "") { if (!ReferenceEquals(e, a)) Fail_("Expected same instance", msg); }
        public static void Greater(IComparable a, IComparable b, string msg = "") { if (Cmp(a, b) <= 0) Fail_($"Expected {a} > {b}", msg); }
        public static void GreaterOrEqual(IComparable a, IComparable b, string msg = "") { if (Cmp(a, b) < 0) Fail_($"Expected {a} >= {b}", msg); }
        public static void Less(IComparable a, IComparable b, string msg = "") { if (Cmp(a, b) >= 0) Fail_($"Expected {a} < {b}", msg); }
        public static void LessOrEqual(IComparable a, IComparable b, string msg = "") { if (Cmp(a, b) > 0) Fail_($"Expected {a} <= {b}", msg); }
        public static T Throws<T>(TestDelegate d, string msg = "") where T : Exception
        {
            try { d(); } catch (T ex) { return ex; } catch (Exception ex) { Fail_($"Expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}", msg); }
            Fail_($"Expected {typeof(T).Name} but nothing was thrown", msg); return null;
        }
        public static void DoesNotThrow(TestDelegate d, string msg = "") { try { d(); } catch (Exception ex) { Fail_("Unexpected " + ex.GetType().Name + ": " + ex.Message, msg); } }
        public static void That(bool c, string msg = "") => IsTrue(c, msg);
        public static void That(object actual, IConstraint c, string msg = "") { if (!c.Matches(actual)) Fail_($"<{actual}> does not satisfy {c}", msg); }

        internal static bool Same(object e, object a)
        {
            if (e == null || a == null) return e == null && a == null;
            if (IsNumeric(e) && IsNumeric(a)) return Convert.ToDouble(e) == Convert.ToDouble(a);
            if (e is IEnumerable ee && a is IEnumerable ae && !(e is string) && !(a is string)) return ee.Cast<object>().SequenceEqual(ae.Cast<object>(), new LooseEq());
            return e.Equals(a);
        }
        static bool IsNumeric(object o) => o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong || o is float || o is double || o is decimal;
        static int Cmp(IComparable a, IComparable b) => IsNumeric(a) && IsNumeric(b) ? Convert.ToDouble(a).CompareTo(Convert.ToDouble(b)) : a.CompareTo(b);
        static string Fmt(string m, object[] a) => a == null || a.Length == 0 ? m : string.Format(m, a);
        class LooseEq : IEqualityComparer<object> { public new bool Equals(object x, object y) => Same(x, y); public int GetHashCode(object o) => 0; }
    }

    public delegate void TestDelegate();

    public static class CollectionAssert
    {
        public static void IsEmpty(IEnumerable e, string msg = "") { var l = e.Cast<object>().ToList(); if (l.Count > 0) throw new AssertionException($"Expected empty but had {l.Count}: {string.Join(" | ", l.Take(5))} {msg}"); }
        public static void IsNotEmpty(IEnumerable e, string msg = "") { if (!e.Cast<object>().Any()) throw new AssertionException("Expected not empty " + msg); }
        public static void Contains(IEnumerable e, object item, string msg = "") { if (!e.Cast<object>().Any(x => Assert.Same(item, x))) throw new AssertionException($"Expected to contain <{item}> {msg}"); }
        public static void DoesNotContain(IEnumerable e, object item, string msg = "") { if (e.Cast<object>().Any(x => Assert.Same(item, x))) throw new AssertionException($"Expected not to contain <{item}> {msg}"); }
        public static void AreEqual(IEnumerable e, IEnumerable a, string msg = "") { if (!Assert.Same(e, a)) throw new AssertionException($"Collections differ: [{string.Join(",", e.Cast<object>())}] vs [{string.Join(",", a.Cast<object>())}] {msg}"); }
        public static void AreEquivalent(IEnumerable e, IEnumerable a, string msg = "") { var x = e.Cast<object>().Select(o => o?.ToString()).OrderBy(s => s); var y = a.Cast<object>().Select(o => o?.ToString()).OrderBy(s => s); if (!x.SequenceEqual(y)) throw new AssertionException("Collections not equivalent " + msg); }
        public static void AllItemsAreUnique(IEnumerable e, string msg = "") { var l = e.Cast<object>().ToList(); if (l.Distinct().Count() != l.Count) throw new AssertionException("Duplicate items " + msg); }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual, string msg = "") { if (actual == null || !actual.Contains(expected)) throw new AssertionException($"Expected \"{actual}\" to contain \"{expected}\" {msg}"); }
        public static void DoesNotContain(string expected, string actual, string msg = "") { if (actual != null && actual.Contains(expected)) throw new AssertionException($"Expected \"{actual}\" NOT to contain \"{expected}\" {msg}"); }
        public static void StartsWith(string expected, string actual, string msg = "") { if (actual == null || !actual.StartsWith(expected)) throw new AssertionException($"Expected \"{actual}\" to start with \"{expected}\" {msg}"); }
        public static void IsMatch(string pattern, string actual, string msg = "") { if (actual == null || !Regex.IsMatch(actual, pattern)) throw new AssertionException($"Expected \"{actual}\" to match /{pattern}/ {msg}"); }
    }

    public interface IConstraint { bool Matches(object actual); }
    class FuncConstraint : IConstraint { readonly Func<object, bool> f; readonly string d; public FuncConstraint(Func<object, bool> f, string d) { this.f = f; this.d = d; } public bool Matches(object a) => f(a); public override string ToString() => d; }
    public static class Is
    {
        public static IConstraint InRange(IComparable lo, IComparable hi) => new FuncConstraint(a => Convert.ToDouble(a) >= Convert.ToDouble(lo) && Convert.ToDouble(a) <= Convert.ToDouble(hi), $"in range [{lo},{hi}]");
        public static IConstraint EqualTo(object v) => new FuncConstraint(a => Assert.Same(v, a), $"equal to {v}");
        public static IConstraint GreaterThan(IComparable v) => new FuncConstraint(a => Convert.ToDouble(a) > Convert.ToDouble(v), $"> {v}");
        public static IConstraint LessThan(IComparable v) => new FuncConstraint(a => Convert.ToDouble(a) < Convert.ToDouble(v), $"< {v}");
        public static IConstraint True => new FuncConstraint(a => a is bool b && b, "true");
        public static IConstraint False => new FuncConstraint(a => a is bool b && !b, "false");
        public static IConstraint Null => new FuncConstraint(a => a == null, "null");
        public static IConstraint Empty => new FuncConstraint(a => a is IEnumerable e && !e.Cast<object>().Any(), "empty");
    }
}
