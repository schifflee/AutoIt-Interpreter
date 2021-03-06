﻿using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

using Piglet.Parser.Construction;
using Piglet.Lexer;

namespace Piglet.Parser
{
    internal class LRParser<T>
        : IParser<T>
    {
        private readonly int errorTokenNumber;
        private readonly int endOfInputTokenNumber;
        private readonly string[] terminalDebugNames;


        internal LRParser(IParseTable<T> parseTable, int errorTokenNumber, int endOfInputTokenNumber, string[] terminalDebugNames)
        {
            this.ParseTable = parseTable;
            this.errorTokenNumber = errorTokenNumber;
            this.endOfInputTokenNumber = endOfInputTokenNumber;
            this.terminalDebugNames = terminalDebugNames;
        }

        public IParseTable<T> ParseTable { get; }

        public ILexer<T> Lexer { get; set; }

        private T Parse(ILexerInstance<T> lexerInstance)
        {
            Stack<T> valueStack = new Stack<T>();
            Stack<int> parseStack = new Stack<int>();
            // This holds the last exception we found when parsing, since we will need to pass this to an error handler once the proper handler has been found
            ParseException exception = null;

            // Push default state onto the parse stack. Default state is always 0
            parseStack.Push(0);

            Tuple<int, T> input = lexerInstance.Next();

            while (true)
            {
                int state = parseStack.Peek();
                int action = ParseTable.Action[state, input.Item1];

                if (action >= 0)
                {
                    if (action == short.MaxValue)
                        return valueStack.Pop(); // Accept!

                    // Shift
                    parseStack.Push(input.Item1);   // Push token unto stack
                    parseStack.Push(action);        // Push state unto stack

                    // Shift token value unto value stack
                    valueStack.Push(input.Item2);

                    // Lex next token
                    input = lexerInstance.Next();
                }
                else if (action == short.MinValue)
                {
                    // Get the expected tokens
                    string[] expectedTokens = GetExpectedTokenNames(state).ToArray();

                    // Create an exception that either might be thrown or may be handed to the error handling routine.
                    exception = new ParseException($"Illegal token '{terminalDebugNames[input.Item1]}', Expected '{string.Join(",", expectedTokens)}'.")
                    {
                        LexerState = lexerInstance,
                        FoundToken = terminalDebugNames[input.Item1],
                        ExpectedTokens = expectedTokens,
                        FoundTokenId = input.Item1,
                        ParserState = state
                    };

                    // Go for error recovery!
                    while (ParseTable.Action[parseStack.Peek(), errorTokenNumber] == short.MinValue)
                        if (parseStack.Count <= 2)
                            throw exception; // If we run out of stack while searching for the error handler, throw the exception. This is what happens when there is no error handler defined at all.
                        else
                        {
                            parseStack.Pop(); // Pop state
                            parseStack.Pop(); // Pop token
                            valueStack.Pop(); // Pop whatever value
                        }

                    // Shift the error token unto the stack
                    state = parseStack.Peek();

                    parseStack.Push(errorTokenNumber);
                    parseStack.Push(ParseTable.Action[state, errorTokenNumber]);
                    valueStack.Push(default);

                    state = parseStack.Peek();

                    // We have now found a state where error recovery is enabled. This means that we 
                    // continue to scan the input stream looking for something which is accepted.
                    // End of input will cause the exception to be thrown
                    while ((ParseTable.Action[state, input.Item1] == short.MinValue) && (input.Item1 != endOfInputTokenNumber))
                        input = lexerInstance.Next(); // nom nom nom

                    // Ran out of file looking for the end of the error rule
                    if (input.Item1 == endOfInputTokenNumber)
                        throw exception;

                    // If we get here we are pretty cool, continue running the parser. The actual error recovery routine will be
                    // called as soon as the error rule itself is reduced.
                }
                else
                {
                    // Get the right reduction rule to apply
                    IReductionRule<T> reductionRule = ParseTable.ReductionRules[-(action + 1)];

                    for (int i = 0; i < reductionRule.NumTokensToPop * 2; ++i)
                        parseStack.Pop();

                    // Transfer to state found in goto table
                    int stateOnTopOfStack = parseStack.Peek();

                    parseStack.Push(reductionRule.TokenToPush);
                    parseStack.Push(ParseTable.Goto[stateOnTopOfStack, reductionRule.TokenToPush]);

                    // Get tokens off the value stack for the OnReduce function to run on
                    T[] onReduceParams = new T[reductionRule.NumTokensToPop];

                    // Need to do it in reverse since thats how the stack is organized
                    for (int i = reductionRule.NumTokensToPop - 1; i >= 0; --i)
                        onReduceParams[i] = valueStack.Pop();

                    // This calls the reduction function with the possible exception set. The exception could be cleared here, but
                    // there is no real reason to do so, since all the normal rules will ignore it, and all the error rules are guaranteed
                    // to have the exception set prior to entering the reduction function.
                    Func<ParseException, T[], T> reduceFunc = reductionRule.OnReduce;

                    valueStack.Push(reduceFunc == null ? default : reduceFunc(exception, onReduceParams));
                }
            }
        }

        private IEnumerable<string> GetExpectedTokenNames(int state) => terminalDebugNames.Where((_, i) => ParseTable.Action[state, i] != short.MinValue);

        public T Parse(string input) => Parse(Lexer.Begin(input));

        public T Parse(TextReader input) => Parse(Lexer.Begin(input));
    }
}
