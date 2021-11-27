﻿/* This file is part of Expresso
 *
 * Copyright (c) 2021 Tom Wimmenhove. All rights reserved.
 * Licensed under the MIT license. See LICENSE file in the project root for details.
 */

using System;
using ExpressoSharp;

namespace Calculator
{
    class TestClass{
        public int Bla {get; set;}
    }
    class Program
    {
        static void Main(string[] args)
        {
            // var t = new TestClass();
            // var prop = t.GetType().GetProperty("Bla");
            // var getter = (Func<TestClass, int>) Delegate.CreateDelegate(typeof(Func<TestClass, int>), prop.GetGetMethod());

            // t.Bla = 42;

            // Console.WriteLine(getter(t));

            // return;
            //var v1 = ExpressoVariable.Create<string>("s", "test");
            var v1 = new ExpressoVariable<string>("s", "test");

            var calc1 = ExpressoCompiler.CompileExpression<Action>("s = \"tost\"", new[] { v1 });
            var calc2 = ExpressoCompiler.CompileExpression<Func<double, string>>("s += \"icle\"", new[] { v1 }, "x");
            calc1();

            var q = calc2(12);

            //return;

            if (args.Length > 1)
            {
                Console.Error.WriteLine("The only accepted argument is the name of the system type to use.");
                Environment.Exit(1);
            }

            /* Use doubles by default */
            if (args.Length == 0)
            {
                var calc = new Calc<double>(false);
                calc.Run();

                return;
            }

            /* Create an instance of Calc<> with the given type */
            var valuetypeName = args[0];

            /* Special case for dynamic type */
            if (valuetypeName == "dynamic")
            {
                var calc = new Calc<dynamic>(true);
                calc.Run();
                
                return;
            }

            var valueType = Type.GetType(valuetypeName);
            if (valueType == null)
            {
                Console.Error.WriteLine($"{valuetypeName} is not a known type");
                Environment.Exit(1);
            }

            Console.WriteLine($"Using type: {valueType}");

            var calcType = typeof(Calc<>).MakeGenericType(valueType);
            var instance = Activator.CreateInstance(calcType, args: new object[] { false });
            instance.GetType().GetMethod(nameof(Calc<object>.Run)).Invoke(instance, null);
        }
    }
}
