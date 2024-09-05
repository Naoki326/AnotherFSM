//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.13.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from C:/Work/StateMachine/Engine/StateMachineScript.g4 by ANTLR 4.13.1

// Unreachable code detected
#pragma warning disable 0162
// The variable '...' is assigned but its value is never used
#pragma warning disable 0219
// Missing XML comment for publicly visible type or member '...'
#pragma warning disable 1591
// Ambiguous reference in cref attribute
#pragma warning disable 419

using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using IToken = Antlr4.Runtime.IToken;

/// <summary>
/// This interface defines a complete generic visitor for a parse tree produced
/// by <see cref="StateMachineScriptParser"/>.
/// </summary>
/// <typeparam name="Result">The return type of the visit operation.</typeparam>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.13.1")]
[System.CLSCompliant(false)]
public interface IStateMachineScriptVisitor<Result> : IParseTreeVisitor<Result> {
	/// <summary>
	/// Visit a parse tree produced by <see cref="StateMachineScriptParser.machine"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitMachine([NotNull] StateMachineScriptParser.MachineContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="StateMachineScriptParser.namespace"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="StateMachineScriptParser.expression"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExpression([NotNull] StateMachineScriptParser.ExpressionContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefState</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_statement"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefState([NotNull] StateMachineScriptParser.DefStateContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefState2</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_statement"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefState2([NotNull] StateMachineScriptParser.DefState2Context context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefGroupState</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_statement"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefGroupState([NotNull] StateMachineScriptParser.DefGroupStateContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefBranch</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefBranch([NotNull] StateMachineScriptParser.DefBranchContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefBranch2</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefBranch2([NotNull] StateMachineScriptParser.DefBranch2Context context);
	/// <summary>
	/// Visit a parse tree produced by the <c>PosDef</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitPosDef([NotNull] StateMachineScriptParser.PosDefContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>ColorDef</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitColorDef([NotNull] StateMachineScriptParser.ColorDefContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>TypeDef</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitTypeDef([NotNull] StateMachineScriptParser.TypeDefContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>FlowIDDef</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.state_branch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFlowIDDef([NotNull] StateMachineScriptParser.FlowIDDefContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="StateMachineScriptParser.position"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitPosition([NotNull] StateMachineScriptParser.PositionContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>POSX</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.posx"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitPOSX([NotNull] StateMachineScriptParser.POSXContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>POSY</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.posy"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitPOSY([NotNull] StateMachineScriptParser.POSYContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefEvent</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.event_statement"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefEvent([NotNull] StateMachineScriptParser.DefEventContext context);
	/// <summary>
	/// Visit a parse tree produced by the <c>DefTransition</c>
	/// labeled alternative in <see cref="StateMachineScriptParser.transition"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context);
}
