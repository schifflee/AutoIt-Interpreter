﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System;

using Unknown6656.AutoIt3.Extensibility;

namespace Unknown6656.AutoIt3.Runtime
{
    /// <summary>
    /// Represents an AutoIt3 macro.
    /// Macros are identified using their case-insensitive name and a '@'-prefix.
    /// </summary>
    public class Macro
        : IEquatable<Macro>
    {
        /// <summary>
        /// The macros's upper-case name without the '@'-prefix.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The interpreter instance with which the current macro is associated.
        /// </summary>
        public Interpreter Interpreter { get; }

        public virtual bool IsKnownMacro => this is KnownMacro;


        internal Macro(Interpreter interpreter, string name)
        {
            Interpreter = interpreter;
            Name = name.TrimStart('@').ToUpperInvariant();
        }

        public virtual Variant GetValue(CallFrame frame)
        {
            Interpreter.MacroResolver.GetTryValue(frame, this, out Variant value);

            return value;
        }

        public override string ToString() => '@' + Name;

        public override int GetHashCode() => Name.GetHashCode();

        public override bool Equals(object? obj) => obj is Macro macro && Equals(macro);

        public bool Equals(Macro? other) => string.Equals(Name, other?.Name);
    }

    public sealed class KnownMacro
        : Macro
    {
        private readonly Func<Interpreter, Variant> _value_provider;


        /// <summary>
        /// Returns the value stored inside the current macro.
        /// </summary>
        public Variant Value => _value_provider(Interpreter);


        internal KnownMacro(Interpreter interpreter, string name, Func<Interpreter, Variant> value_provider)
            : base(interpreter, name) => _value_provider = value_provider;

        public override Variant GetValue(CallFrame frame) => Value;
    }

    public sealed class MacroResolver
    {
        private readonly HashSet<KnownMacro> _macros = new();

        
        public Interpreter Interpreter { get; }

        public int KnownMacroCount => _macros.Count;

        public ImmutableHashSet<KnownMacro> KnownMacros => _macros.ToImmutableHashSet();


        internal MacroResolver(Interpreter interpreter) => Interpreter = interpreter;

        internal void AddKnownMacro(KnownMacro macro) => _macros.Add(macro);

        public bool HasMacro(CallFrame frame, string macro_name) => GetTryValue(frame, macro_name, out _);

        public bool HasMacro(CallFrame frame, Macro macro) => GetTryValue(frame, macro, out _);
 
        public bool GetTryValue(CallFrame frame, Macro macro, out Variant value) => GetTryValue(frame, macro.Name, out value);

        public bool GetTryValue(CallFrame frame, string macro_name, out Variant value)
        {
            value = Variant.Null;
            macro_name = macro_name.TrimStart('@');

            foreach (KnownMacro macro in _macros)
                if (macro.Name.Equals(macro_name, StringComparison.InvariantCultureIgnoreCase))
                {
                    value = macro.Value;

                    return true;
                }

            foreach (AbstractMacroProvider provider in Interpreter.PluginLoader.MacroProviders)
                if (provider.ProvideMacroValue(frame, macro_name, out Variant? v) && v.HasValue)
                {
                    value = v.Value;

                    return true;
                }

            return false;
        }

        public override string ToString() => $"{KnownMacroCount} known macros.";
    }
}