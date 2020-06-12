﻿using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;

using Unknown6656.AutoIt3.ExpressionParser;
using Unknown6656.AutoIt3.Extensibility;
using Unknown6656.Common;

using Piglet.Parser.Configuration.Generic;
using Piglet.Lexer;
using System.Diagnostics;

namespace Unknown6656.AutoIt3.Runtime
{
    public sealed class AU3Thread
        : IDisposable
    {
        private static volatile int _tid = 0;
        private readonly ConcurrentStack<CallFrame> _callstack = new ConcurrentStack<CallFrame>();
        private volatile bool _running = false;


        public Interpreter Interpreter { get; }

        public CallFrame? CurrentFrame => _callstack.TryPeek(out CallFrame? lp) ? lp : null;

        public SourceLocation? CurrentLocation => CurrentFrame switch {
            AU3CallFrame f => f.CurrentLocation,
            _ => SourceLocation.Unknown
        };

        public ScriptFunction? CurrentFunction => CurrentFrame?.CurrentFunction;

        public bool IsDisposed { get; private set; }

        public bool IsMainThread => ReferenceEquals(this, Interpreter.MainThread);

        public int ThreadID { get; }


        internal AU3Thread(Interpreter interpreter)
        {
            ThreadID = ++_tid;
            Interpreter = interpreter;
            Interpreter.AddThread(this);

            Program.PrintDebugMessage($"Created thread {this}");
        }

        public InterpreterError? Start(ScriptFunction function)
        {
            if (_running)
                return InterpreterError.WellKnown(CurrentLocation, "error.thread_already_running", ThreadID);
            else
                _running = true;

            InterpreterError? res = Call(function);

            _running = false;

            return res;
        }

        internal InterpreterError? Call(ScriptFunction function)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(AU3Thread));

            CallFrame? old = CurrentFrame;
            CallFrame frame = function switch
            {
                AU3Function f => new AU3CallFrame(this, f),
                NativeFunction f => new NativeCallFrame(this, f),
                _ => throw new ArgumentException($"A function of the type '{function}' cannot be handled by the current thread '{this}'.", nameof(function)),
            };

            _callstack.Push(frame);

            InterpreterError? result = frame.Exec();

            while (!ReferenceEquals(CurrentFrame, old))
                _callstack.TryPop(out _);

            return result;
        }

        internal SourceLocation? ExitCall()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(AU3Thread));

            _callstack.TryPop(out _);

            return CurrentLocation;
        }

        public override string ToString() => $"0x{_tid:x4}{(IsMainThread ? " (main)" : "")} @ {CurrentLocation}";

        public void Dispose()
        {
            if (IsDisposed)
                return;
            else
                IsDisposed = true;

            Interpreter.RemoveThread(this);
            _callstack.TryPop(out _);

            Program.PrintDebugMessage($"Disposed thread {this}");

            if (!_callstack.IsEmpty)
                throw new InvalidOperationException("The execution stack is not empty.");
        }
    }

    public abstract class CallFrame
    {
        public AU3Thread CurrentThread { get; }

        public ScriptFunction CurrentFunction { get; }

        public Interpreter Interpreter => CurrentThread.Interpreter;


        internal CallFrame(AU3Thread thread, ScriptFunction function)
        {
            CurrentThread = thread;
            CurrentFunction = function;
        }

        internal abstract InterpreterError? Exec();

        public InterpreterError? Call(ScriptFunction function) => CurrentThread.Call(function);
    }

    public sealed class NativeCallFrame
        : CallFrame
    {
        internal NativeCallFrame(AU3Thread thread, NativeFunction function)
            : base(thread, function)
        {
        }

        internal override InterpreterError? Exec() => (CurrentFunction as NativeFunction)?.Execute(this);
    }

    public sealed class AU3CallFrame
        : CallFrame
    {
        private volatile int _instruction_pointer = 0;
        private (SourceLocation LineLocation, string LineContent)[] _line_cache;


        public SourceLocation CurrentLocation => _line_cache[_instruction_pointer].LineLocation;

        public string CurrentLineContent => _line_cache[_instruction_pointer].LineContent;


        internal AU3CallFrame(AU3Thread thread, AU3Function function)
            : base(thread, function)
        {
            _line_cache = function.Lines;
            _instruction_pointer = 0;
        }

        internal override InterpreterError? Exec()
        {
            ScannedScript script = CurrentFunction.Script;
            InterpreterError? result = null;

            if (CurrentFunction.IsMainFunction)
                result = script.LoadScript(this);

            Program.PrintDebugMessage($"Executing {CurrentFunction}");

            while (_instruction_pointer < _line_cache.Length)
                if (result is null)
                {
                    result = ParseCurrentLine()?.OptionalError;

                    if (!MoveNext())
                        break;
                }
                else
                    break;

            if (CurrentFunction.IsMainFunction)
                result ??= script.UnLoadScript(this);

            return result;
        }

        private bool MoveNext()
        {
            if (_instruction_pointer < _line_cache.Length)
            {
                ++_instruction_pointer;

                return true;
            }
            else
                return false;
        }

        public InterpreterResult? ParseCurrentLine()
        {
            (SourceLocation loc, string line) = _line_cache[_instruction_pointer];
            InterpreterResult? result = null;

            if (string.IsNullOrWhiteSpace(line))
                return InterpreterResult.OK;

            Program.PrintDebugMessage($"({loc}) {line}");

            result ??= ProcessDirective(line);
            result ??= ProcessStatement(line);
            result ??= ProcessExpressionStatement(line);
            result ??= UseExternalLineProcessors(line);

            return result ?? WellKnownError("error.unparsable_line", line);
        }

        private InterpreterResult? ProcessDirective(string directive)
        {
            if (!directive.StartsWith('#'))
                return null;

            directive = directive[1..];

            if (directive.Match(@"^include(?<once>-once)?\s+(?<open>[""'<])(?<path>(?:(?!\k<close>).)+)(?<close>[""'>])$", out ReadOnlyIndexer<string, string>? g))
            {
                char open = g["open"][0];
                char close = g["close"][0];

                if (open != close && open != '<' && close != '>')
                    return WellKnownError("error.mismatched_quotes", open, close);

                ScriptScanningOptions options = ScriptScanningOptions.Regular;

                if (g["once"].Contains('-'))
                    options |= ScriptScanningOptions.IncludeOnce;

                if (open != '<')
                    options |= ScriptScanningOptions.RelativePath;

                return Interpreter.ScriptScanner.ScanScriptFile(CurrentLocation, g["path"], options).Match(err => err, script => Call(script.MainFunction));
            }

            InterpreterResult? result = null;

            foreach (AbstractDirectiveProcessor? proc in Interpreter.PluginLoader.DirectiveProcessors)
                result ??= proc?.ProcessDirective(this, directive);

            return result?.IsOK ?? false ? null : WellKnownError("error.unparsable_dirctive", directive);
        }




        // TODO
        private readonly ConcurrentStack<(BlockStatementType, SourceLocation)> _blockstatement_stack = new ConcurrentStack<(BlockStatementType, SourceLocation)>();


        private void PushBlockStatement(BlockStatementType statement) => _blockstatement_stack.Push((statement, CurrentLocation));

        private InterpreterError? PopBlockStatement(params BlockStatementType[] accepted)
        {
            if (accepted.Length == 0)
                _blockstatement_stack.TryPop(out _);
            else
            {
                _blockstatement_stack.TryPop(out (BlockStatementType type, SourceLocation loc) statement);

                if (!accepted.Contains(statement.type))
                    return WellKnownError("error.no_matching_close", statement.type, statement.loc);
            }

            return null;
        }

        private InterpreterResult? ProcessStatement(string line)
        {
            InterpreterResult? result = line.Match(null, new Dictionary<string, Func<Match, InterpreterResult?>>
            {
                [@"^for\s+(?<start>.+)\s+to\s+(?<stop>.+)(\s+step\s+(?<step>.+))?$"] = m =>
                {
                    throw new NotImplementedException();
                },
                [@"^for\s+(?<variable>.+)\s+in\s+(?<expression>.+)$"] = m =>
                {
                    throw new NotImplementedException();
                },
                [@"^with\s+(?<expression>.+)$"] = m =>
                {
                    throw new NotImplementedException();
                },
                [@"^redim\s+(?<expression>.+)$"] = m =>
                {
                    throw new NotImplementedException();
                },

                ["^next$"] = _ => PopBlockStatement(BlockStatementType.For, BlockStatementType.ForIn),
                ["^wend$"] = _ => PopBlockStatement(BlockStatementType.While),
                ["^endwith$"] = _ => PopBlockStatement(BlockStatementType.With),
                ["^endswitch$"] = _ => PopBlockStatement(BlockStatementType.Switch, BlockStatementType.Case),
                ["^endselect$"] = _ => PopBlockStatement(BlockStatementType.Select, BlockStatementType.Case),

                [@"^continueloop\s*(?<level>\d+)?\s*$"] = m =>
                {
                    int level = int.TryParse(m.Groups["level"].Value, out int l) ? l : 1;
                    InterpreterResult? result = InterpreterResult.OK;

                    while (level-- > 1)
                        result = PopBlockStatement(BlockStatementType.For, BlockStatementType.ForIn, BlockStatementType.While, BlockStatementType.Do);

                     // TODO : continue


                     throw new NotImplementedException();
                },
                [@"^exitloop\s*(?<level>\d+)?\s*$"] = m =>
                {
                    int level = int.TryParse(m.Groups["level"].Value, out int l) ? l : 1;
                    InterpreterResult? result = InterpreterResult.OK;

                    while (level-- > 0)
                        result = PopBlockStatement(BlockStatementType.For, BlockStatementType.ForIn, BlockStatementType.While, BlockStatementType.Do);

                    throw new NotImplementedException();

                    return result;
                },
            });

            // TODO

            foreach (AbstractStatementProcessor? proc in Interpreter.PluginLoader.StatementProcessors)
                if (proc is { Regex: string pat } sp && line.Match(pat, out Match _))
                    result ??= sp.ProcessStatement(this, line);

            return result;
        }

        private InterpreterError? ProcessDeclarationModifiers(ref string line, out DeclarationType declaration_type, out (char op, int amount)? enum_step)
        {
            declaration_type = DeclarationType.None;
            enum_step = null;

            while (line.Match(@"^(local|static|global|const|dim|enum|step)\b", out Match m_modifier))
            {
                DeclarationType modifier = (DeclarationType)Enum.Parse(typeof(DeclarationType), m_modifier.Value, true);

                if (declaration_type.HasFlag(modifier))
                    return WellKnownError("error.duplicate_modifier", modifier);

                if (modifier is DeclarationType.Step)
                    if (line.Match(@"^(?<op>[+\-*]?)(?<step>\d+)\b", out Match m_step))
                    {
                        char op = '+';
                        int amount = int.Parse(m_step.Groups["step"].Value);

                        if (m_step.Groups["op"] is { Length: > 0, Value: string s })
                            op = s[0];

                        enum_step = (op, amount);
                    }
                    else
                        return WellKnownError("error.invalid_step", new string(line.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray()));

                declaration_type |= modifier;
                line = line[..m_modifier.Length].TrimStart();
            }

            if (declaration_type.HasFlag(DeclarationType.Step) && !declaration_type.HasFlag(DeclarationType.Enum))
                return WellKnownError("error.unexpected_step");

            foreach ((DeclarationType m1, DeclarationType m2) in new[]
            {
                (DeclarationType.Local, DeclarationType.Global),
                (DeclarationType.Static, DeclarationType.Const),
                // TODO : ?
            })
                if (declaration_type.HasFlag(m1) && declaration_type.HasFlag(m2))
                    return WellKnownError("error.incomplatible_modifiers", m1, m2);

            return null;
        }

        private InterpreterError? ProcessExpressionStatement(string line)
        {
            try
            {
                if (ProcessDeclarationModifiers(ref line, out DeclarationType declaration_type, out (char op, int amount)? enum_step) is { } err)
                    return err;

                ParserResult<AST.PARSABLE_EXPRESSION>? parser_result = (declaration_type is DeclarationType.None ? ParserProvider.ExpressionParser : ParserProvider.MultiDeclarationParser).Parse(line);
                AST.PARSABLE_EXPRESSION? expression = parser_result.ParsedValue;

                Program.PrintDebugMessage($"Parsed \"{expression.ToString().Replace('\n', ' ')}\"");

                if (declaration_type == DeclarationType.None)
                    return ProcessAssignmentStatement(expression);
                else if (expression is AST.PARSABLE_EXPRESSION.MultiDeclarationExpression multi_decl)
                    return ProcessMultiDeclarationExpression(multi_decl, declaration_type, enum_step);
                else
                    return WellKnownError("error.invalid_multi_decl", line);
            }
            catch (LexerException ex)
            {
                return WellKnownError("error.invalid_syntax", line, ex.Message);
            }
        }

        private InterpreterError? ProcessMultiDeclarationExpression(AST.PARSABLE_EXPRESSION.MultiDeclarationExpression multi_decl, DeclarationType decltype, (char op, int amount)? enum_step)
        {

        }

        private InterpreterError? ProcessAssignmentStatement(AST.PARSABLE_EXPRESSION result)
        {
            (AST.ASSIGNMENT_TARGET target, AST.OPERATOR_ASSIGNMENT @operator, AST.EXPRESSION expression) = Cleanup.CleanUpExpression(result);


            Console.WriteLine($"{target} {@operator} {expression}");

            return null; // success
        }

        private InterpreterResult? UseExternalLineProcessors(string line)
        {
            foreach (AbstractLineProcessor? proc in Interpreter.PluginLoader.LineProcessors)
                if ((proc?.CanProcessLine(line) ?? false) && proc?.ProcessLine(this, line) is { } res)
                    return res;

            return null;
        }

        private InterpreterError WellKnownError(string key, params object[] args) => InterpreterError.WellKnown(CurrentLocation, key, args);
    }

    [Flags]
    public enum DeclarationType
        : byte
    {
        None = 0b_0000_0000,
        Dim = 0b_0000_0001,
        Local = 0b_0000_0010,
        Global = 0b_0000_0100,
        Const = 0b_0000_1000,
        Static = 0b_0001_0000,
        Enum = 0b_0010_0000,
        Step = 0b_0100_0000,
    }

    public enum BlockStatementType
        : int
    {
        Global,
        Func,
        With,
        For,
        ForIn,
        While,
        Do,
        If,
        ElseIf,
        Else,
        Select,
        Switch,
        Case,
    }
}
