global $a = Default + (Null * "42"), $b = 42, $c = 7


ConsoleWrite(@SW_UNLOCK + " " + @CRLF)
exit






$x = "X"
$y = "Y"
ConsoleWrite($x & " " & $y & @CRLF)
swap($x,$y)
ConsoleWrite($x & " " & $y & @CRLF)

Func Swap(ByRef $vVar1, ByRef $vVar2)
   ; DebugVar($vVar1)
   ; DebugVar($vVar2)

   Local $vTemp = $vVar1
   $vVar1 = $vVar2
   $vVar2 = $vTemp
EndFunc


Exit










; Local $arr[] = [8, 4, 5, 9, 1]

ConsoleWrite()


$b = 9 + $xxxx
$c = test($b)
TEST(8)

ConsoleWrite($b & @CRLF & $c);

func test($b = 9)
   local const $a = -9
   $b = 42
endfunc

