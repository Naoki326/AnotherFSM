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
	: 'def' STRING 'as' STRING? 'state'
	'{'
		state_branch*
	'}'																		#DefState
	| 'def' STRING '(' STRING? ')'
	'{'
		state_branch*
	'}'																		#DefState2
;

state_branch
	: 'branch' INT TRIGGER STRING	 SEMICOLON								#DefBranch
	| branch_type=(
	INT | NONE | SUCCESS | FAILED | ERROR | BREAK | CANCEL
	) TRIGGER STRING	 SEMICOLON											#DefBranch2
	| 'Pos' COLON position	SEMICOLON										#PosDef
    | 'Color' COLON CODESTRING	 SEMICOLON									#ColorDef
    | 'Type' COLON STRING	 SEMICOLON										#TypeDef
    | 'FlowID' COLON GUID	 SEMICOLON										#FlowIDDef
;

state_group
	: STRING 'to' STRING SEMICOLON											#GroupDef
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

// GUID格式: 8-4-4-4-12 的十六进制数字，用连字符分隔
GUID : HEX8 '-' HEX4 '-' HEX4 '-' HEX4 '-' HEX12;

// 可选的：匹配带花括号的GUID格式
// BRACED_GUID : '{' HEX8 '-' HEX4 '-' HEX4 '-' HEX4 '-' HEX12 '}';

// 辅助片段定义
fragment HEX8 : HEX HEX HEX HEX HEX HEX HEX HEX;
fragment HEX4 : HEX HEX HEX HEX;
fragment HEX12 : HEX HEX HEX HEX HEX HEX HEX HEX HEX HEX HEX HEX;
fragment HEX : [0-9a-fA-F];

CODESTRING : '"'.*?'"' ;

INT : '-'?'0'..'9'+ ;

DOUBLE : '-'?[0-9]+('.'[0-9]+)? ;

WS  : [ \t\r\n]+ -> channel(1) ;

COMMENT : '/*'.*?'*/' -> channel(2);

LINE_COMMENT :'//'  ~ ('\n' | '\r') *  '\r'? '\n'  -> channel(2);