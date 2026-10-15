using System;
using System.Reflection;
using NUnit.Framework;

internal static class ProtocolRegression
{
    private static int Main()
    {
        int passed = 0;
        try
        {
            var instance = new SerialInputProtocolTests();
            foreach (MethodInfo method in typeof(SerialInputProtocolTests).GetMethods())
            {
                object[] cases = method.GetCustomAttributes(typeof(TestCaseAttribute), true);
                if (cases.Length > 0)
                {
                    foreach (TestCaseAttribute test in cases) { method.Invoke(instance, test.Arguments); passed++; }
                }
                else if (method.IsDefined(typeof(TestAttribute), true)) { method.Invoke(instance, null); passed++; }
            }
            Console.WriteLine("SERIAL_PROTOCOL_REGRESSION_PASS: " + passed + " existing NUnit cases");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
