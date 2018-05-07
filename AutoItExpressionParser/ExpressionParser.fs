﻿namespace AutoItExpressionParser

open AutoItExpressionParser.ExpressionAST

open System.Text.RegularExpressions
open System.Globalization


type 'a cslist = System.Collections.Generic.List<'a>

type private TernaryReduceHint =
    | TernaryRight
    | TernaryMiddle
    | TernaryLeft
type private BinaryReduceHint =
    | BinaryRight
    | BinaryLeft
type private UnaryReduceHint =
    | UnaryPrefix
    | UnaryPostfix

type ExpressionParser(optimize : bool, assignment : bool, declaration : bool) =
    inherit AbstractParser<MULTI_EXPRESSION list>()
    member x.UseOptimization = optimize
    member x.AllowAssignment = assignment
    member x.DeclarationMode = declaration
    override x.BuildParser() =
        let lparse p (f : string -> long) (s : string) =
            let s = s.TrimStart('+').ToLower().Replace(p, "")
            let n, s = if s.[0] = '-' then (true, s.Substring(1))
                        else (false, s)
            let l = f s
            if n then -l else l
            |> decimal
            |> Number
            
        let nt_multi_expressions        = x.nt<MULTI_EXPRESSION list>()
        let nt_multi_expression         = x.nt<MULTI_EXPRESSION>()
        let nt_array_indexer            = x.nt<EXPRESSION>()
        let nt_array_indexers           = x.nt<EXPRESSION list>()
        let nt_array_init_expressions   = x.nt<EXPRESSION list>()
        let nt_array_init_expression    = x.nt<EXPRESSION list>()
        let nt_expression_ext           = x.nt<EXPRESSION>()
        let nt_expression               = x.nt<EXPRESSION>()
        let nt_subexpression            = Array.map (fun _ -> x.nt<EXPRESSION>()) [| 0..22 |]
        let nt_funccall                 = x.nt<FUNCCALL>()
        let nt_funcparams               = x.nt<EXPRESSION list>()
        let nt_literal                  = x.nt<LITERAL>()
        let nt_assignment_expression    = x.nt<ASSIGNMENT_EXPRESSION>()
        let nt_operator_binary_ass      = x.nt<OPERATOR_ASSIGNMENT>()
        let nt_dot_members              = x.nt<MEMBER list>()
        let nt_dot_member               = x.nt<MEMBER list>()
        
        let t_operator_assign_nand      = x.t @"~&&="
        let t_operator_assign_and       = x.t @"&&="
        let t_operator_assign_nxor      = x.t @"~^^="
        let t_operator_assign_xor       = x.t @"^^="
        let t_operator_assign_nor       = x.t @"~\|\|="
        let t_operator_assign_or        = x.t @"\|\|="
        let t_operator_assign_rol       = x.t @"<<<="
        let t_operator_assign_ror       = x.t @">>>="
        let t_operator_assign_shl       = x.t @"<<="
        let t_operator_assign_shr       = x.t @">>="
        let t_operator_assign_add       = x.t @"\+="
        let t_operator_assign_sub       = x.t @"-="
        let t_operator_assign_mul       = x.t @"\*="
        let t_operator_assign_div       = x.t @"/="
        let t_operator_assign_mod       = x.t @"%="
        let t_operator_assign_con       = x.t @"&="
        let t_operator_assign_pow       = x.t @"^="
        let t_operator_bit_nand         = x.t @"~&&"
        let t_operator_bit_and          = x.t @"&&"
        let t_operator_bit_nxor         = x.t @"~^^"
        let t_operator_bit_xor          = x.t @"^^"
        let t_operator_bit_nor          = x.t @"~\|\|"
        let t_operator_bit_or           = x.t @"\|\|"
        let t_operator_bit_rol          = x.t @"<<<"
        let t_operator_bit_ror          = x.t @">>>"
        let t_operator_bit_shl          = x.t @"<<"
        let t_operator_bit_shr          = x.t @">>"
        let t_operator_bit_not          = x.t @"~"
        let t_operator_comp_neq         = x.t @"<>"
        let t_operator_comp_gte         = x.t @">="
        let t_operator_comp_gt          = x.t @">"
        let t_operator_comp_lte         = x.t @"<="
        let t_operator_comp_lt          = x.t @"<"
        let t_operator_comp_eq          = x.t @"=="
        let t_operator_at1              = x.t @"@\|"
        let t_operator_at0              = x.t @"@"
        let t_operator_dotrange         = x.t @"\.?\.\."
        let t_symbol_equal              = x.t @"="
        let t_symbol_numbersign         = x.t @"#"
        let t_symbol_questionmark       = x.t @"\?" // TODO
        let t_symbol_colon              = x.t @":" // TODO
        let t_symbol_dot                = x.t @"\."
        let t_symbol_comma              = x.t @","
        let t_symbol_minus              = x.t @"-"
        let t_symbol_plus               = x.t @"\+"
        let t_symbol_asterisk           = x.t @"\*"
        let t_symbol_slash              = x.t @"/"
        let t_symbol_percent            = x.t @"%"
        let t_symbol_hat                = x.t @"^"
        let t_symbol_ampersand          = x.t @"&"
        let t_symbol_oparen             = x.t @"\("
        let t_symbol_cparen             = x.t @"\)"
        let t_symbol_obrack             = x.t @"\["
        let t_symbol_cbrack             = x.t @"\]"
        let t_symbol_ocurly             = x.t @"\{"
        let t_symbol_ccurly             = x.t @"\}"
        let t_keyword_to                = x.t @"to"
        let t_keyword_impl              = x.t @"impl"
        let t_keyword_nand              = x.t @"nand"
        let t_keyword_nor               = x.t @"nor"
        let t_keyword_and               = x.t @"and"
        let t_keyword_nxor              = x.t @"nxor"
        let t_keyword_xor               = x.t @"xor"
        let t_keyword_or                = x.t @"or"
        let t_keyword_not               = x.t @"(not|!)"
        let t_literal_true              = x.tf @"true"                                  (fun _ -> True)
        let t_literal_false             = x.tf @"false"                                 (fun _ -> False)
        let t_literal_null              = x.tf @"null"                                  (fun _ -> Null)
        let t_literal_default           = x.tf @"default"                               (fun _ -> Default)
        let t_hex                       = x.tf @"(\+|-)?(0x[\da-f]+|[\da-f]h)"          (lparse "0x" (fun s -> long.Parse(s.TrimEnd 'h', NumberStyles.HexNumber)))
        let t_bin                       = x.tf @"(\+|-)?0b[01]+"                        (lparse "0b" (fun s -> System.Convert.ToInt64(s, 2)))
        let t_oct                       = x.tf @"(\+|-)?0o[0-7]+"                       (lparse "0o" (fun s -> System.Convert.ToInt64(s, 8)))
        let t_dec                       = x.tf @"(\+|-)?\d+(\.\d+)?(e(\+|-)?\d+)?"      (fun s -> match decimal.TryParse s with
                                                                                                  | (true, d) -> d
                                                                                                  | _ -> decimal.Parse(s, NumberStyles.Float)
                                                                                                  |> Number
                                                                                        ) 
        let t_variable                  = x.tf @"$[a-z_]\w*"                            (fun s -> VARIABLE(s.Substring 1))
        let t_macro                     = x.tf @"@[a-z_]\w*"                            (fun s -> MACRO(s.Substring 1))
        let t_string_1                  = x.tf "\"(([^\"]*\"\"[^\"]*)*|[^\"]+)\""       (fun s -> String(s.Remove(s.Length - 1).Remove(0, 1).Trim().Replace("\"\"", "\"")))
        let t_string_2                  = x.tf @"'(([^']*''[^']*)*|[^']+)'"             (fun s -> String(s.Remove(s.Length - 1).Remove(0, 1).Trim().Replace("''", "'")))
        let t_string_3                  = x.tf @"$""(([^""]*\\""[^""]*)*|[^""]+)"""     (fun s -> 
                                                                                             let mutable s = s.Remove(s.Length - 1)
                                                                                                              .Remove(0, 2)
                                                                                                              .Trim()
                                                                                             let r = Regex(@"(?<!\\)(?:\\{2})*((?<type>\$|@)(?<var>[a-z_]\w*)\b)", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
                                                                                             let l = cslist<EXPRESSION>()
                                                                                             let proc (s : string) =
                                                                                                 let s = s.Replace(@"\\", "\ufffe")
                                                                                                          .Replace(@"\""", "\"")
                                                                                                          .Replace(@"\r", "\r")
                                                                                                          .Replace(@"\n", "\n")
                                                                                                          .Replace(@"\t", "\t")
                                                                                                          .Replace(@"\v", "\v")
                                                                                                          .Replace(@"\b", "\b")
                                                                                                          .Replace(@"\a", "\a")
                                                                                                          .Replace(@"\f", "\f")
                                                                                                          .Replace(@"\d", "\x7f")
                                                                                                          .Replace(@"\0", "\0")
                                                                                                          .Replace(@"\$", "$")
                                                                                                          .Replace(@"\@", "@")
                                                                                                          .Replace(@"\ufffe", "\\")
                                                                                                 let r p s = Regex.Replace(
                                                                                                                 s,
                                                                                                                 p,
                                                                                                                 (fun (m : Match) -> (char.ConvertFromUtf32(int.Parse(m.Groups.["code"].ToString(), NumberStyles.HexNumber))).ToString()),
                                                                                                                 RegexOptions.Compiled ||| RegexOptions.IgnoreCase
                                                                                                             )
                                                                                                 s
                                                                                                 |> r @"\\u(?<code>[0-9a-f]{4})"
                                                                                                 |> r @"\\x(?<code>[0-9a-f]{2})"
                                                                                                 |> String
                                                                                                 |> Literal
                                                                                             while r.IsMatch s do
                                                                                                 let mt = (r.Match s).Groups
                                                                                                 let t = mt.["type"].ToString() = "$"
                                                                                                 let m = mt.["var"]
                                                                                                 l.Add(proc (s.Remove (m.Index - 1)))
                                                                                                 l.Add(if t then m.ToString()
                                                                                                                 |> VARIABLE
                                                                                                                 |> VariableExpression
                                                                                                       else m.ToString()
                                                                                                            |> MACRO
                                                                                                            |> Macro)
                                                                                                 s <- s.Substring (m.Index + m.Length)
                                                                                             l
                                                                                             |> Seq.toList
                                                                                             |> List.reduce (fun x y -> BinaryExpression (StringConcat, x, y))
                                                                                        )
        let t_identifier                = x.tf @"[_a-z]\w*"                             id

        let (!@) x = nt_subexpression.[x]


        (*
        Operator precedence:
            ?:
            impl
            nor
            or
            nxor
            xor
            nand
            and
            <=
            <
            >=
            >
            <>
            =       (cis)
            ==      (cs)
            &
            @|..
            @..
            @|
            @
            ~||
            ||
            ~^^
            ^^
            ~&&
            &&
            <<<
            >>>
            <<
            >>
            +
            -
            %
            /
            *
            ^
            #       (u, post)
            !       (u, pre)
            -       (u, pre)
            +       (u, pre)
            ~       (u, pre)
            .x      (u, post)
            x(y)    (u, post)
            [x]     (u, post)
            (x)     (u, infx)
            funccall
            macro
            variable
            string_3
            literal
        *)
        
        let reducet i h x y f = let n = !@(i + 1)
                                let c = !@i
                                let f = (fun a _ b _ c -> f a b c)
                                reduce0 c n
                                match h with
                                | TernaryLeft -> reduce5 c c x n y n f
                                | TernaryMiddle -> reduce5 c n x c y n f
                                | TernaryRight -> reduce5 c n x n y c f
        let reduceb i h x f = let n = !@(i + 1)
                              let c = !@i
                              let f = (fun a _ b -> f a b)
                              reduce0 c n
                              match h with
                              | BinaryLeft -> reduce3 c c x n f
                              | BinaryRight -> reduce3 c n x c f
        let reducebe i h x f = reduceb i h x (fun a b -> BinaryExpression(f, a, b))
        let reduceu i h x f = reduce0 !@i !@(i + 1)
                              match h with
                              | UnaryPrefix -> reduce2 !@i x !@i (fun _ e -> f e)
                              | UnaryPostfix -> reduce2 !@i !@i x (fun e _ -> f e)
        let reduceue i h x f = reduceu i h x (fun e -> UnaryExpression(f, e))
                              
        reduce1 nt_expression !@0 (fun e -> if x.UseOptimization then Analyzer.ProcessExpression e else e)
        reducet 0 TernaryRight t_symbol_questionmark t_symbol_colon (fun c x y -> TernaryExpression(c, x, y))
        reduceb 1 BinaryLeft t_keyword_impl (fun a b -> BinaryExpression(Or, UnaryExpression(Not, a), b))
        reducebe 2 BinaryLeft t_keyword_nor Nor
        reducebe 3 BinaryLeft t_keyword_or Or
        reducebe 4 BinaryLeft t_keyword_nxor Nxor
        reducebe 5 BinaryLeft t_keyword_xor Xor
        reducebe 6 BinaryLeft t_keyword_nand Nand
        reducebe 7 BinaryLeft t_keyword_and And
        reducebe 8 BinaryLeft t_operator_comp_lte LowerEqual
        reducebe 9 BinaryLeft t_operator_comp_lt Lower
        reducebe 10 BinaryLeft t_operator_comp_gte GreaterEqual
        reducebe 11 BinaryLeft t_operator_comp_gt Greater
        reducebe 12 BinaryLeft t_operator_comp_neq Unequal
        reducebe 13 BinaryLeft t_symbol_equal EqualCaseInsensitive
        reducebe 14 BinaryLeft t_operator_comp_eq EqualCaseSensitive
        reducebe 15 BinaryLeft t_keyword_nor StringConcat
        reducet 16 TernaryLeft t_operator_at1 t_operator_dotrange (fun e s l -> UnaryExpression(String1Index(s, l), e))
        reducet 17 TernaryLeft t_operator_at0 t_operator_dotrange (fun e s l -> UnaryExpression(String1Index(BinaryExpression(Add, s, Literal <| Number 1m), l), e))
        reduceb 18 BinaryLeft t_operator_at1 (fun e i -> UnaryExpression(String1Index(i, Literal <| Number 1m), e))
        reduceb 19 BinaryLeft t_operator_at0 (fun e i -> UnaryExpression(String1Index(BinaryExpression(Add, i, Literal <| Number 1m), Literal <| Number 1m), e))
        reducebe 20 BinaryLeft t_operator_bit_nor BitwiseNor
        reducebe 21 BinaryLeft t_operator_bit_or BitwiseOr
        reducebe 22 BinaryLeft t_operator_bit_nxor BitwiseNxor
        reducebe 23 BinaryLeft t_operator_bit_xor BitwiseXor
        reducebe 24 BinaryLeft t_operator_bit_nand BitwiseNand
        reducebe 25 BinaryLeft t_operator_bit_and BitwiseAnd
        reducebe 26 BinaryLeft t_operator_bit_rol BitwiseRotateLeft
        reducebe 27 BinaryLeft t_operator_bit_ror BitwiseRotateRight
        reducebe 28 BinaryLeft t_operator_bit_shl BitwiseShiftLeft
        reducebe 29 BinaryLeft t_operator_bit_shr BitwiseShiftRight
        reducebe 30 BinaryLeft t_symbol_plus Add
        reducebe 31 BinaryLeft t_symbol_minus Subtract
        reducebe 32 BinaryLeft t_symbol_percent Modulus
        reducebe 33 BinaryLeft t_symbol_slash Divide
        reducebe 34 BinaryLeft t_symbol_asterisk Multiply
        reducebe 35 BinaryRight t_symbol_hat Power
        reduceue 36 UnaryPostfix t_symbol_numbersign StringLength
        reduceue 37 UnaryPrefix t_keyword_not Not
        reduceue 38 UnaryPrefix t_symbol_minus Negate
        reduceue 39 UnaryPrefix t_symbol_plus Identity
        reduceue 40 UnaryPrefix t_operator_bit_not BitwiseNot
        reduce0 !@41 !@42
        reduce2 !@41 !@42 nt_dot_members (fun e m -> DotAccess(e, m))
        reduce0 !@42 !@43
        reduce2 !@42 !@42 // TODO : λ-call
        
        reduce2 nt_dot_member t_symbol_dot t_identifier (fun _ e -> [Field e])
        reduce2 nt_dot_member t_symbol_dot nt_funccall (fun _ e -> [Method e])
        
        reduce1 nt_dot_members nt_dot_member id
        reduce2 nt_dot_members nt_dot_members nt_dot_member (@)





















        failwith "Not yet implemented....."
        //////////////////////////////////////////////////////////////////////////////////////////////// TODO : REPAIR EVERYTHING BELOW THIS LINE ////////////////////////////////////////////////////////////////////////////////////////////////
        
        let reduce_m x = reduce0 !@x !@(x + 1)
                         List.iter (fun e -> reduce3 !@x !@x (fst e) nt_expression (*!@(x + 1)*) (snd e))
        let reduce_mm = List.iter (fun e -> reduce_m (fst e) (snd e))
        


        
        
        //reduce3 !@18 !@19 t_symbol_dot nt_dot_members (fun e _ m -> DotAccess(e, m))
        reduce0 !@18 !@19
        
        reduce4 !@19 !@19 t_symbol_oparen nt_funcparams t_symbol_cparen (fun e _ p _ -> ΛFunctionCall(e, p))
        reduce3 !@19 !@19 t_symbol_oparen t_symbol_cparen (fun e _ _ -> ΛFunctionCall(e, []))
        reduce0 !@19 !@20
        
        reduce0 !@20 !@21
        reduce2 !@20 nt_expression nt_array_indexer (fun e i -> ArrayAccess(e, i))
        
        reduce3 !@21 t_symbol_oparen nt_expression t_symbol_cparen (fun _ e _ -> e)
        reduce0 !@21 !@22

        reduce1 !@22 t_variable VariableExpression
        reduce1 !@22 nt_funccall FunctionCall
        reduce1 !@22 nt_literal Literal
        reduce1 !@22 t_macro Macro
        reduce0 !@22 t_string_3


        reduce4 nt_funccall t_identifier t_symbol_oparen nt_funcparams t_symbol_cparen (fun f _ p _ -> (f, p))
        reduce3 nt_funccall t_identifier t_symbol_oparen t_symbol_cparen (fun f _ _ -> (f, []))

        reduce3 nt_funcparams nt_expression t_symbol_comma nt_funcparams (fun e _ p -> e::p)
        reduce1 nt_funcparams nt_expression (fun e -> [e])
        
        reduce3 nt_array_indexer t_symbol_obrack nt_expression t_symbol_cbrack (fun _ e _ -> e)
        reduce1 nt_array_indexers nt_array_indexer (fun e -> [e])
        reduce2 nt_array_indexers nt_array_indexer nt_array_indexers (@)

        reduce0 nt_literal t_literal_true
        reduce0 nt_literal t_literal_false
        reduce0 nt_literal t_literal_null
        reduce0 nt_literal t_literal_default
        reduce0 nt_literal t_string_1
        reduce0 nt_literal t_string_2
        reduce0 nt_literal t_hex
        reduce0 nt_literal t_dec
        reduce0 nt_literal t_oct
        reduce0 nt_literal t_bin
        
        if x.AllowAssignment then
            reduce1 nt_operator_binary_ass t_operator_assign_sub (fun _ -> AssignSubtract)
            reduce1 nt_operator_binary_ass t_operator_assign_add (fun _ -> AssignAdd)
            reduce1 nt_operator_binary_ass t_operator_assign_mul (fun _ -> AssignMultiply)
            reduce1 nt_operator_binary_ass t_operator_assign_div (fun _ -> AssignDivide)
            reduce1 nt_operator_binary_ass t_operator_assign_mod (fun _ -> AssignModulus)
            reduce1 nt_operator_binary_ass t_operator_assign_con (fun _ -> AssignConcat)
            reduce1 nt_operator_binary_ass t_operator_assign_pow (fun _ -> AssignPower)
            reduce1 nt_operator_binary_ass t_operator_assign_nand (fun _ -> AssignNand)
            reduce1 nt_operator_binary_ass t_operator_assign_and (fun _ -> AssignAnd)
            reduce1 nt_operator_binary_ass t_operator_assign_nxor (fun _ -> AssignNxor)
            reduce1 nt_operator_binary_ass t_operator_assign_xor (fun _ -> AssignXor)
            reduce1 nt_operator_binary_ass t_operator_assign_nor (fun _ -> AssignNor)
            reduce1 nt_operator_binary_ass t_operator_assign_or (fun _ -> AssignOr)
            reduce1 nt_operator_binary_ass t_operator_assign_rol (fun _ -> AssignRotateLeft)
            reduce1 nt_operator_binary_ass t_operator_assign_ror (fun _ -> AssignRotateRight)
            reduce1 nt_operator_binary_ass t_operator_assign_shl (fun _ -> AssignShiftLeft)
            reduce1 nt_operator_binary_ass t_operator_assign_shr (fun _ -> AssignShiftRight)
            reduce1 nt_operator_binary_ass t_symbol_equal (fun _ -> Assign)
        
            reduce4 nt_assignment_expression t_variable nt_array_indexers nt_operator_binary_ass nt_expression (fun v i o e -> ArrayAssignment(o, v, i, e))
            reduce3 nt_assignment_expression t_variable nt_operator_binary_ass nt_expression (fun v o e -> ScalarAssignment(o, v, e))

            

        //////////////////////////////////////////////////////////////////////////////////////////////// TODO ////////////////////////////////////////////////////////////////////////////////////////////////

        reduce1 nt_multi_expressions nt_multi_expression (fun m -> [m])
        reduce3 nt_multi_expressions nt_multi_expression t_symbol_comma nt_multi_expressions (fun m _ ms -> m::ms)
        reduce1 nt_multi_expression nt_expression_ext SingleValue

        if x.DeclarationMode then
            reduce1 nt_expression_ext t_variable VariableExpression
        

            // TODO : array init exprssions 
        

            reduce3 nt_array_init_expressions t_symbol_obrack nt_array_init_expression t_symbol_cbrack (fun _ es _ -> es)
            reduce2 nt_array_init_expressions t_symbol_obrack t_symbol_cbrack (fun _ _ -> [])

            reduce3 nt_array_init_expression nt_expression t_symbol_comma nt_array_init_expression (fun e _ es -> e::es)
            reduce1 nt_array_init_expression nt_expression (fun e -> [e])
        else
            reduce3 nt_multi_expression nt_expression_ext t_keyword_to nt_expression_ext (fun a _ b -> ValueRange(a, b))
            reduce0 nt_expression_ext nt_expression

        if x.AllowAssignment || x.DeclarationMode then
            reduce1 nt_expression_ext nt_assignment_expression AssignmentExpression

        //////////////////////////////////////////////////////////////////////////////////////////////// TODO ////////////////////////////////////////////////////////////////////////////////////////////////

        



        x.Configuration.LexerSettings.Ignore <- [| @"[\r\n\s]+" |]
        x.Configuration.LexerSettings.IgnoreCase <- true
