grammar StateMachineScript;

/*
 * Parser Rules
 */
 
machine
    : expression*
	| namespace*
	| EOF
;

namespace
	:'namespace' STRING
	'{'
		expression*
	'}'
;

expression
	: state_statement
	| event_statement
	| transition
	| state_branch
;

state_statement
	: 'def' STRING 'as' STRING 'state'
	'{'
		state_branch*
	'}'																		#DefState
	| 'def' STRING '(' STRING? ')'
	'{'
		state_branch*
	'}'																		#DefState2
	| 'def' STRING
	'{'
		STRING 'to' STRING
	'}'																		#DefGroupState
;

state_branch
	: 'branch' INT TRIGGER STRING	 SEMICOLON								#DefBranch
	| branch_type=(
	INT | NONE | SUCCESS | FAILED | ERROR | BREAK | CANCEL
	) TRIGGER STRING	 SEMICOLON											#DefBranch2
	| 'Pos' COLON position	SEMICOLON										#PosDef
    | 'Color' COLON CODESTRING	 SEMICOLON									#ColorDef
    | 'Type' COLON STRING	 SEMICOLON										#TypeDef
    | 'FlowID' COLON (STRING|INT)	 SEMICOLON								#FlowIDDef
;

position
	: '(' posx ',' posy ')'
;

posx
	: (DOUBLE|INT)															#POSX
;

posy
	: (DOUBLE|INT)															#POSY
;

event_statement
	: 'def' STRING ('as')? 'event' SEMICOLON								#DefEvent
;

transition
	: STRING TRIGGER STRING 'to' STRING SEMICOLON							#DefTransition
;


/*
 * Lexer Rules
 */

NONE : 'none';
NEXT : 'next';
SUCCESS : 'success';
FAILED : 'failed';
ERROR : 'error';
BREAK : 'break';
CANCEL : 'cancel';

TRIGGER : ('->' | 'trigger') ;

COLON : ':' ;

SEMICOLON : ';' ;

STRING : [_A-Za-z\u0391-\u03A9\u03B1-\u03C9\u4e00-\u9fa5][0-9_A-Za-z\u0391-\u03A9\u03B1-\u03C9\u4e00-\u9fa5]* ; 

CODESTRING : '"'.*?'"' ;

INT : '-'?'0'..'9'+ ;

DOUBLE : '-'?[0-9]+('.'[0-9]+)? ;

WS  : [ \t\r\n]+ -> channel(1) ;

COMMENT : '/*'.*?'*/' -> channel(2);

LINE_COMMENT :'//'  ~ ('\n' | '\r') *  '\r'? '\n'  -> channel(2);