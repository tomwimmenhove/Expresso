﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expresso
{
    public class CompilerException : Exception
    {
        public IEnumerable<Diagnostic> Diagnostics { get; }

        public CompilerException(string message, IEnumerable<Diagnostic> diagnostics)
             : base(message)
        {
            Diagnostics = diagnostics;
        }
    }

    internal static class TypeExtensions
    {
        internal static TypeSyntax ToTypeSyntax(this Type type) =>
            type == typeof(void)
                ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
                : SyntaxFactory.ParseTypeName(type.FullName);
    }

    public class ExpressoParameter
    {
        public string Name { get; }
        public Type Type { get; }

        public ExpressoParameter(string name, Type type)
        {
            Name = name;
            Type = type;
        }

        internal ParameterSyntax ToParameterSyntax() =>
            SyntaxFactory.Parameter(SyntaxFactory.Identifier(Name))
                .WithType(SyntaxFactory.ParseTypeName(Type.FullName));
    }

    public class ExpressoMethod
    {
        public string Name { get; }
        public Type ReturnType { get; }
        public string Expression { get;}
        public ExpressoParameter[] Parameters { get; }

        public ExpressoMethod(string name, Type returnType, string expression,
            params ExpressoParameter[] parameters)
        {
            Name = name;
            ReturnType = returnType;
            Expression = expression;
            Parameters = parameters;
        }

        internal MethodDeclarationSyntax ToMethodDeclarationSyntax()
        {
            var returnStatement = SyntaxFactory.ParseExpression(Expression);
            var expressionDiagnostics = returnStatement.GetDiagnostics();

            if (expressionDiagnostics.Any())
            {
                throw new CompilerException("Compilation failed", expressionDiagnostics);
            }

            return SyntaxFactory.MethodDeclaration(ReturnType.ToTypeSyntax(), Name).AddModifiers(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword)).AddParameterListParameters(
                    Parameters.Select(x => x.ToParameterSyntax()).ToArray())
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ReturnStatement(returnStatement)));
        }
    }

    public class ExpressionCompiler
    {
        public static T CompileExpression<T>(string expression, params string[] parameterNames) where T : Delegate
        {
            var method = CreateMethodDeclarationSyntax<T>("SingleMethod", expression, parameterNames);

            var allTypes = new HashSet<Type>(method.Parameters.Select(x => x.Type));
            if (method.ReturnType != typeof(void))
            {
                allTypes.Add(method.ReturnType);
            }
            allTypes.Add(typeof(object));

            var compilationUnit = CreateCompilationUnitSyntax("SingleNameSpace", "SingleClass",
                method.ToMethodDeclarationSyntax());

            //System.Diagnostics.Debug.WriteLine(compilationUnit.NormalizeWhitespace().ToString());

            var assembly = Compile(compilationUnit.SyntaxTree, allTypes);

            var member = assembly.GetType("SingleNameSpace.SingleClass").GetMember("SingleMethod");

            return (T) Delegate.CreateDelegate(typeof(T), null, (MethodInfo)member[0]);
        }

        private static Assembly Compile(SyntaxTree syntaxTree, IEnumerable<Type> usedTypes)
        {
            var references = usedTypes.Select(x => MetadataReference.CreateFromFile(x.Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "InMemoryAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    throw new CompilerException("Compilation failed", result.Diagnostics);
                }

                ms.Seek(0, SeekOrigin.Begin);

                return Assembly.Load(ms.ToArray());
            }
        }

        private static ExpressoMethod CreateMethodDeclarationSyntax<T>(string name, string expression, params string[] parameterNames) where T : Delegate
        {
            var invokeMethod = typeof(T).GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();
            if (parameters.Count() != parameterNames.Count())
            {
                throw new ArgumentException($"Number of parameter names ({parameters.Count()}) does not match the numbers of parameters of the delegate type ({parameterNames.Count()})");
            }

            var expressoParameters = new ExpressoParameter[parameters.Count()];
            for (var i = 0; i < parameters.Count(); i++)
            {
                expressoParameters[i] = new ExpressoParameter(parameterNames[i], parameters[i].ParameterType);
            }

            return new ExpressoMethod(name, invokeMethod.ReturnType, expression, expressoParameters);
        }

        private static MethodDeclarationSyntax CreateMethodDeclarationSyntax(
            string name, Type returnType, string expression,
            params ExpressoParameter[] parameters)
        {
            var returnStatement = SyntaxFactory.ParseExpression(expression);
            var expressionDiagnostics = returnStatement.GetDiagnostics();

            if (expressionDiagnostics.Any())
            {
                throw new CompilerException("Compilation failed", expressionDiagnostics);
            }

            return SyntaxFactory.MethodDeclaration(returnType.ToTypeSyntax(), name).AddModifiers(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword)).AddParameterListParameters(
                    parameters.Select(x => x.ToParameterSyntax()).ToArray())
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ReturnStatement(returnStatement)));
        }

        private static CompilationUnitSyntax CreateCompilationUnitSyntax(string nameSpaceName, string className,
            params MethodDeclarationSyntax[] methods) =>
            SyntaxFactory.CompilationUnit().AddMembers
            (
                SyntaxFactory.NamespaceDeclaration(SyntaxFactory.IdentifierName(nameSpaceName)).AddMembers
                (
                    SyntaxFactory.ClassDeclaration(className).AddMembers(methods)
                )
            );
    }

    public class NonNativeTypeTest
    {
        public int X { get; set; }

        public NonNativeTypeTest(int x)
        {
            X = x;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                var calc1 = ExpressionCompiler.CompileExpression<Func<NonNativeTypeTest, double>>("x.X * 21", "x");
                sw.Stop();

                Console.WriteLine($"First compilation took {sw.Elapsed}");

                sw.Reset();
                sw.Start();
                var calc2 = ExpressionCompiler.CompileExpression<Func<double, NonNativeTypeTest>>(
                    "new Expresso.NonNativeTypeTest((int) x * 21)", "x");
                sw.Stop();

                Console.WriteLine($"Second compilation took {sw.Elapsed}");

                Console.WriteLine(calc1(new NonNativeTypeTest(2)));
                Console.WriteLine(calc2(4).X);
            }
            catch (CompilerException e)
            {
                var failures = e.Diagnostics.Where(diagnostic =>
                    diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                foreach (Diagnostic diagnostic in failures)
                {
                    Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                }
            }
        }
    }
}
