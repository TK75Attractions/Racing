using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using NUnit.Framework;

// Small offline runner for the same NUnit tests Unity uses; no package restore required.
public static class RunTests
{
    public static int Main(string[] args)
    {
        int passed = 0, failed = 0;
        var results = new List<Tuple<string, string, string>>();
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        foreach (MethodInfo method in type.GetMethods())
        {
            if (!Attribute.IsDefined(method, typeof(TestAttribute))) continue;
            string name = type.Name + "." + method.Name;
            try
            {
                method.Invoke(Activator.CreateInstance(type), null);
                passed++; results.Add(Tuple.Create(name, "Passed", ""));
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                Exception actual = exception.InnerException ?? exception;
                failed++; results.Add(Tuple.Create(name, "Failed", actual.ToString()));
                Console.WriteLine("FAIL " + name + "\n" + actual);
            }
        }
        if (args.Length > 0)
        using (XmlWriter xml = XmlWriter.Create(args[0], new XmlWriterSettings { Indent = true }))
        {
            xml.WriteStartElement("test-run");
            xml.WriteAttributeString("result", failed == 0 ? "Passed" : "Failed");
            xml.WriteAttributeString("total", (passed + failed).ToString());
            xml.WriteAttributeString("passed", passed.ToString()); xml.WriteAttributeString("failed", failed.ToString());
            foreach (var result in results)
            {
                xml.WriteStartElement("test-case"); xml.WriteAttributeString("name", result.Item1);
                xml.WriteAttributeString("result", result.Item2);
                if (result.Item3.Length > 0) xml.WriteElementString("failure", result.Item3);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        Console.WriteLine("Tests: " + passed + " passed, " + failed + " failed");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}
