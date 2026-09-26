using System; using BoosterWatch;
class T{static int f=0; static void Check(bool c,string n){Console.WriteLine((c?"PASS: ":"FAIL: ")+n); if(!c)f++;}
static void Main(){ var L=new RecoveryLimits{SinkSpeed=12,HorizontalSpeed=3,AngularSpeed=0.5}; string r;
Check(CutoffPolicy.Evaluate(3.7,0.3,0.03,0.1,1.5,L,20,out r)==TouchdownOutcome.Safe,"Flug 20:01 (3,7 m/s, 0,3 seitlich) wird geborgen");
Check(CutoffPolicy.Evaluate(13,0.3,0.03,0.1,1.5,L,20,out r)==TouchdownOutcome.Crashed,"zu schnell -> keine Bergung ("+r+")");
Check(CutoffPolicy.Evaluate(3,5,0.03,0.1,1.5,L,20,out r)==TouchdownOutcome.Crashed,"seitlich 5 -> keine Bergung ("+r+")");
Check(CutoffPolicy.Evaluate(3,1,0.03,25,1.5,L,20,out r)==TouchdownOutcome.Crashed,"25 Grad Neigung -> keine Bergung ("+r+")");
Check(CutoffPolicy.Evaluate(3,1,0.9,2,1.5,L,20,out r)==TouchdownOutcome.Crashed,"Drehung -> keine Bergung");
Check(CutoffPolicy.Evaluate(3,1,0.1,2,10,L,20,out r)==TouchdownOutcome.Unconfirmed,"Abschaltung zu hoch -> Kontakt entscheidet");
Check(CutoffPolicy.Evaluate(double.NaN,1,0.1,2,1,L,20,out r)==TouchdownOutcome.Unconfirmed,"NaN -> unklar");
Check(!CutoffPolicy.RecoverNow(TouchdownOutcome.Safe,100,100.5,false),"vor Kontakt und Wartezeit nicht bergen");
Check(CutoffPolicy.RecoverNow(TouchdownOutcome.Safe,100,100.2,true),"bei Kontakt sofort bergen");
Check(CutoffPolicy.RecoverNow(TouchdownOutcome.Safe,100,101.6,false),"nach Wartezeit bergen");
Check(!CutoffPolicy.RecoverNow(TouchdownOutcome.Crashed,100,101.6,true),"Absturzurteil nie bergen");
Environment.Exit(f);}}
