﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace EsotericIDE.Languages
{
    partial class Quipu
    {
        private sealed class quipuExecutionEnvironment : ExecutionEnvironment
        {
            private program _program;
            public List<object> ThreadValues;
            public int CurrentThread;
            public int CurrentKnot;
            public Stack<object> Stack;
            public TextReader Input;

            public quipuExecutionEnvironment(program program, string input)
            {
                _program = program;
                ThreadValues = program.Threads.Select(t => (object) BigInteger.Zero).ToList();
                CurrentThread = 0;
                Stack = new Stack<object>();
                Stack.Push(BigInteger.Zero);
                Input = new StringReader(input);
            }

            public override string DescribeExecutionState()
            {
                var threadValues = new[] { "Thread values:" }.Concat(ThreadValues.Select((s, i) => (s is string ? @"{{0}}: ""{0}""".Fmt(((string) s).CLiteralEscape()) : @"{{0}}: {0}".Fmt(s)).Fmt(i))).ToArray();
                var stackValues = new[] { "Stack:" }.Concat(Stack.Select(s => s is string ? @"""{0}""".Fmt(((string) s).CLiteralEscape()) : s.ToString())).ToArray();
                var width = stackValues.Max(v => v.Length);
                return Enumerable.Range(0, Math.Max(threadValues.Length, stackValues.Length))
                    .Select(l => "{{0,{0}}}  {{1}}".Fmt(width).Fmt(l < stackValues.Length ? stackValues[l] : "", l < threadValues.Length ? threadValues[l] : ""))
                    .JoinString(Environment.NewLine);
            }

            public void AddOutput(string str) { _output.Append(str); }

            public BigInteger GetNumber()
            {
                if (Stack.Count == 0)
                    throw new InvalidOperationException("Stack underflow.");
                BigInteger result;
                var raw = Stack.Pop();
                return (raw is BigInteger ? (BigInteger) raw : BigInteger.TryParse((string) raw, out result) ? result : Ut.Throw<BigInteger>(new InvalidOperationException("Value is not a valid integer.")));
            }

            protected override void Run()
            {
                using (var instructionPointer = _program.Execute(this).GetEnumerator())
                {
                    bool canceled = false;
                    RuntimeError error = null;
                    try
                    {
                        while (instructionPointer.MoveNext())
                        {
                            lock (_locker)
                                if (_breakpoints.Any(bp => bp >= instructionPointer.Current.Index && bp < instructionPointer.Current.Index + Math.Max(instructionPointer.Current.Length, 1)))
                                    State = ExecutionState.Debugging;

                            switch (State)
                            {
                                case ExecutionState.Debugging:
                                    fireDebuggerBreak(instructionPointer.Current);
                                    _resetEvent.Reset();
                                    _resetEvent.WaitOne();
                                    continue;
                                case ExecutionState.Running:
                                    continue;
                                case ExecutionState.Stop:
                                    canceled = true;
                                    goto finished;
                                case ExecutionState.Finished:
                                    goto finished;
                                default:
                                    throw new InvalidOperationException("Execution state has invalid value: " + State);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        var type = e.GetType();
                        error = new RuntimeError(instructionPointer.Current, e.Message + (type != typeof(Exception) ? " (" + type.Name + ")" : ""));
                    }

                    finished:
                    fireExecutionFinished(canceled, error);
                    State = ExecutionState.Finished;
                    _resetEvent.Reset();
                }
            }
        }
    }
}
