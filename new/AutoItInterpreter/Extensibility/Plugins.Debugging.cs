﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;

using Unknown6656.AutoIt3.Extensibility.Plugins.Internals;
using Unknown6656.AutoIt3.Runtime;
using Unknown6656.Common;

namespace Unknown6656.AutoIt3.Extensibility.Plugins.Debugging
{
    public sealed class DebuggingFunctionProvider
        : AbstractFunctionProvider
    {
        public override ProvidedNativeFunction[] ProvidedFunctions { get; } = new[]
        {
            ProvidedNativeFunction.Create(nameof(DebugVar), 1, DebugVar),
            ProvidedNativeFunction.Create(nameof(DebugCallFrame), 0, DebugCallFrame),
            ProvidedNativeFunction.Create(nameof(DebugThread), 0, DebugThread),
            ProvidedNativeFunction.Create(nameof(DebugAllVars), 0, DebugAllVars),
            ProvidedNativeFunction.Create(nameof(DebugAllVarsCompact), 0, DebugAllVarsCompact),
            ProvidedNativeFunction.Create(nameof(DebugCodeLines), 0, DebugCodeLines),
            ProvidedNativeFunction.Create(nameof(DebugAllThreads), 0, DebugAllThreads),
        };


        public DebuggingFunctionProvider(Interpreter interpreter)
            : base(interpreter)
        {
        }

        private static IDictionary<string, object?> GetVariantInfo(Variant value)
        {
            string s = value.RawData?.ToString()?.Trim() ?? "";
            string ts = value.RawData?.GetType().ToString() ?? "<void>";

            IDictionary<string, object?> dic = new Dictionary<string, object?>
            {
                ["value"] = value.ToDebugString(),
                ["type"] = value.Type,
                ["raw"] = s != ts ? $"\"{s}\" ({ts})" : ts
            };

            if (value.AssignedTo is Variable variable)
                dic["assignedTo"] = variable;

            if (value.ReferencedVariable is Variable @ref)
                dic["referenceTo"] = GetVariableInfo(@ref);

            return dic;
        }

        private static IDictionary<string, object?> GetVariableInfo(Variable? variable) => new Dictionary<string, object?>
        {
            ["name"] = variable,
            ["constant"] = variable.IsConst,
            ["global"] = variable.IsGlobal,
            ["location"] = variable.DeclaredLocation,
            ["scope"] = variable.DeclaredScope,
            ["value"] = GetVariantInfo(variable.Value)
        };

        private static IDictionary<string, object?> GetCallFrameInfo(CallFrame? frame)
        {
            IDictionary<string, object?> dic = new Dictionary<string, object?>();

            frame = frame?.CallerFrame;

            if (frame is { })
            {
                dic["type"] = frame.GetType().Name;
                dic["thread"] = frame.CurrentThread;
                dic["function"] = frame.CurrentFunction;
                dic["ret.value"] = frame.ReturnValue;
                dic["variables"] = frame.VariableResolver.LocalVariables.ToArray(GetVariableInfo);
                dic["arguments"] = frame.PassedArguments.ToArray(GetVariantInfo);

                if (frame is AU3CallFrame au3)
                {
                    dic["location"] = au3.CurrentLocation;
                    dic["line"] = $"\"{au3.CurrentLineContent}\"";
                }
            }

            return dic;
        }

        private static IDictionary<string, object?> GetThreadInfo(AU3Thread thread) => new Dictionary<string, object?>
        {
            ["id"] = thread.ThreadID,
            ["disposed"] = thread.IsDisposed,
            ["isMain"] = thread.IsMainThread,
            ["running"] = thread.IsRunning,
            ["callstack"] = thread.CallStack.ToArray(GetCallFrameInfo)
        };

        private static IDictionary<string, object?> GetAllVariables(Interpreter interpreter)
        {
            IDictionary<string, object?> dic = new Dictionary<string, object?>();
            List<VariableScope> scopes = new List<VariableScope> { interpreter.VariableResolver };
            int count;

            do
            {
                count = scopes.Count;

                foreach (VariableScope scope in from indexed in scopes.ToArray()
                                                from s in indexed.ChildScopes
                                                where !scopes.Contains(s)
                                                select s)
                    scopes.Add(scope);
            }
            while (count != scopes.Count);

            foreach (VariableScope scope in scopes)
                dic[scope.InternalName] = new Dictionary<string, object?>
                {
                    ["frame"] = scope.CallFrame,
                    ["function"] = scope.CallFrame?.CurrentFunction,
                    ["isGlobal"] = scope.IsGlobalScope,
                    ["parent"] = scope.Parent,
                    ["children"] = scope.ChildScopes.ToArray(c => c.InternalName),
                    ["variables"] = scope.LocalVariables.ToArray(GetVariableInfo),
                };

            return dic;
        }

        private static string SerializeDictionary(IDictionary<string, object?> dic, string title)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(title + ": {");

            void serialize(IDictionary<string, object?> dic, int level)
            {
                int w = dic.Keys.Select(k => k.Length).Append(0).Max();

                foreach (string key in dic.Keys)
                {
                    sb.Append($"{new string(' ', level * 4)}{(key + ':').PadRight(w + 1)} ");

                    switch (dic[key])
                    {
                        case IDictionary<string, object?> d:
                            sb.AppendLine();
                            serialize(d, level + 1);

                            break;
                        case Array { Length: 0 }:
                            sb.Append($"(0)");

                            break;
                        case Array arr:
                            sb.AppendLine($"({arr.Length})");

                            int index = 0;
                            int rad = 1 + (int)Math.Log10(arr.Length);

                            foreach (object? elem in arr)
                            {
                                sb.Append($"{new string(' ', (level + 1) * 4)}[{index.ToString().PadLeft(rad, '0')}]: ");

                                if (elem is IDictionary<string, object?> d)
                                {
                                    sb.AppendLine();
                                    serialize(d, level + 2);
                                }
                                else
                                    sb.Append(elem?.ToString());

                                ++index;
                            }

                            break;
                        case object obj:
                            sb.Append(obj);

                            break;
                    }

                    if (!sb.ToString().EndsWith(Environment.NewLine))
                        sb.AppendLine();
                }
            }

            serialize(dic, 1);

            return sb.AppendLine("}")
                     .ToString();
        }

        private static Variant SerializePrint(CallFrame frame, IDictionary<string, object?> dic, object? title)
        {
            frame.Print(SerializeDictionary(dic, title is string s ? s : title?.ToString() ?? ""));

            return Variant.Zero;
        }

        private static FunctionReturnValue DebugVar(CallFrame frame, Variant[] args) => SerializePrint(frame, GetVariableInfo(args[0].AssignedTo), args[0].AssignedTo);

        private static FunctionReturnValue DebugCallFrame(CallFrame frame, Variant[] args) => SerializePrint(frame, GetCallFrameInfo(frame), "Call Frame");

        private static FunctionReturnValue DebugThread(CallFrame frame, Variant[] _) => SerializePrint(frame, GetThreadInfo(frame.CurrentThread), frame.CurrentThread);

        private static FunctionReturnValue DebugAllVars(CallFrame frame, Variant[] _) => SerializePrint(frame, GetAllVariables(frame.Interpreter), frame.Interpreter);

        private static FunctionReturnValue DebugAllVarsCompact(CallFrame frame, Variant[] _)
        {
            List<VariableScope> scopes = new List<VariableScope> { frame.Interpreter.VariableResolver };
            int count;

            do
            {
                count = scopes.Count;

                foreach (VariableScope scope in from indexed in scopes.ToArray()
                                                from s in indexed.ChildScopes
                                                where !scopes.Contains(s)
                                                select s)
                    scopes.Add(scope);
            }
            while (count != scopes.Count);

            object? netobj = null;
            StringBuilder sb = new StringBuilder();
            IEnumerable<(string, string, string, string)> iterators = from kvp in InternalsFunctionProvider._iterators
                                                                      let index = kvp.Value.index
                                                                      let tuple = kvp.Value.index < kvp.Value.collection.Length ? kvp.Value.collection[kvp.Value.index] : default
                                                                      select (
                                                                          $"/iter/{kvp.Key}",
                                                                          Autoit3.ASM.Name,
                                                                          "Iterator",
                                                                          $"Index:{index}, Length:{kvp.Value.collection.Length}, Key:{tuple.key.ToDebugString()}, Value:{tuple.value.ToDebugString()}"
                                                                      );
            IEnumerable<(string, string, string, string)> global_objs = from id in frame.Interpreter.GlobalObjectStorage.Keys
                                                                        where frame.Interpreter.GlobalObjectStorage.TryGet(id, out netobj)
                                                                        select (
                                                                            $"/objs/{id:x8}",
                                                                            Autoit3.ASM.Name,
                                                                            ".NET Object",
                                                                            netobj?.ToString() ?? "<null>"
                                                                        );
            (string name, string loc, string type, string value)[] variables = (from scope in scopes
                                                                                from variable in scope.LocalVariables
                                                                                let name = scope.InternalName + '$' + variable.Name
                                                                                orderby name ascending
                                                                                select (
                                                                                    name,
                                                                                    variable.DeclaredLocation.ToString(),
                                                                                    variable.Value.Type.ToString(),
                                                                                    variable.Value.ToDebugString()
                                                                                )).Concat(iterators)
                                                                                  .Concat(global_objs)
                                                                                  .ToArray();
            int w_name = variables.Select(t => t.name.Length).Append(4).Max();
            int w_loc = variables.Select(t => t.loc.Length).Append(8).Max();
            int w_type = variables.Select(t => t.type.Length).Append(4).Max();
            int w_value = variables.Select(t => t.value.Length).Append(5).Max();

            w_value = Math.Min(w_value + 3, Math.Min(Console.BufferWidth, Console.WindowWidth) - 6 - w_loc - w_type - w_name);

            Array.Sort(variables, (x, y) =>
            {
                string[] pathx = x.name.Split('/');
                string[] pathy = y.name.Split('/');

                for (int i = 0, l = Math.Min(pathx.Length, pathy.Length); i < l; ++i)
                {
                    bool varx = pathx[i].StartsWith("$");
                    int cmp = varx ^ pathy[i].StartsWith("$") ? varx ? -1 : 1 : pathx[i].CompareTo(pathy[i]);

                    if (cmp != 0)
                        return cmp;
                }

                return string.Compare(x.name, y.name);
            });

            sb.Append($@"{variables.Length} Variables:
┌{new string('─', w_name)}┬{new string('─', w_loc)}┬{new string('─', w_type)}┬{new string('─', w_value)}┐
│{"Name".PadRight(w_name)}│{"Location".PadRight(w_loc)}│{"Type".PadRight(w_type)}│{"Value".PadRight(w_value)}│
├{new string('─', w_name)}┼{new string('─', w_loc)}┼{new string('─', w_type)}┼{new string('─', w_value)}┤
");

            foreach ((string name, string loc, string type, string value) in variables)
            {
                string val = value.Length > w_value ? value[..(w_value - 3)] + "..."  : value.PadLeft(w_value);

                sb.AppendLine($"│{name.PadRight(w_name)}│{loc.PadRight(w_loc)}│{type.PadRight(w_type)}│{val}│");
            }

            sb.AppendLine($"└{new string('─', w_name)}┴{new string('─', w_loc)}┴{new string('─', w_type)}┴{new string('─', w_value)}┘");

            frame.Print(sb.ToString());

            return Variant.Zero;
        }

        private static FunctionReturnValue DebugCodeLines(CallFrame frame, Variant[] _)
        {
            if (frame.CurrentThread.CallStack.OfType<AU3CallFrame>().FirstOrDefault() is AU3CallFrame au3frame)
            {
                StringBuilder sb = new StringBuilder();
                (SourceLocation loc, string txt)[] lines = au3frame.CurrentLineCache;
                int w_num = (int)Math.Max(4, Math.Log10(lines.Length) + 1);
                int w_loc = lines.Select(l => l.loc.ToString().Length).Append(8).Max();
                int w_txt = lines.Select(l => l.txt.ToString().Length).Append(7).Max();
                int cwidth = Math.Min(Console.BufferWidth, Console.WindowWidth);
                int eip = au3frame.CurrentInstructionPointer;

                sb.Append($@"{lines.Length} Lines:
┌{new string('─', w_num)}┬{new string('─', w_loc)}┬{new string('─', w_txt)}┐
│{"Line".PadRight(w_num)}│{"Location".PadRight(w_loc)}│{"Content".PadRight(w_txt)}│
├{new string('─', w_num)}┼{new string('─', w_loc)}┼{new string('─', w_txt)}┤
");

                if (w_num + w_loc + w_txt + 4 > cwidth)
                    w_txt = cwidth - 4 - w_num - w_loc;

                for (int i = 0; i < lines.Length; i++)
                {
                    (SourceLocation loc, string txt) = lines[i];
                    void append(object o) => sb.Append(eip == i ? $"\x1b[7m{o}\x1b[27m" : o);

                    txt = txt.Length > w_txt ? txt[..(w_txt - 3)] + "..." : txt.PadRight(w_txt);

                    sb.Append('│');
                    append(i.ToString().PadLeft(w_num));
                    sb.Append('│');
                    append(loc.ToString().PadRight(w_loc));
                    sb.Append('│');
                    append(txt);
                    sb.AppendLine("│");
                }

                sb.AppendLine($"└{new string('─', w_num)}┴{new string('─', w_loc)}┴{new string('─', w_txt)}┘");

                frame.Print(sb.ToString());
            }

            return Variant.Zero;
        }

        private static FunctionReturnValue DebugAllThreads(CallFrame frame, Variant[] _)
        {
            // TODO

            return Variant.Zero;
        }
    }
}
