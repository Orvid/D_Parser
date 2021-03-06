﻿//
// CompletionProviderVisitor.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2013 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Parser;
using System.Collections.Generic;
using D_Parser.Completion.Providers;
using D_Parser.Resolver.ASTScanner;

namespace D_Parser.Completion
{//TODO: don't show completion on '0.|'
	//TODO: (Type). -- lookup static properties, fields and methods.
	class CompletionProviderVisitor : DefaultDepthFirstVisitor
	{
		#region Properties
		bool halt; 
		public IBlockNode scopedBlock;
		IStatement scopedStatement;
		Stack<MemberFilter> shownKeywords = new Stack<MemberFilter>();
		MemberFilter ShownKeywords {get{return shownKeywords.Count != 0 ? shownKeywords.Peek() : 0;}}

		readonly char triggerChar;
		bool explicitlyNoCompletion;

		bool handlesInitializer;
		DVariable initializedNode;
		bool handlesBaseClasses;
		DClassLike handledClass;
		public const MemberFilter BlockMemberFilter = MemberFilter.BlockKeywords | MemberFilter.Types | MemberFilter.Enums | MemberFilter.TypeParameters;
		public const MemberFilter ExprMemberFilter = MemberFilter.ExpressionKeywords | MemberFilter.All;

		public AbstractCompletionProvider GeneratedProvider { 
			get{
				if (prv != null)
					return prv;

				if (explicitlyNoCompletion && triggerChar != '\0')
					return null;

				var vis = MemberFilter.All;

				if (!(scopedBlock is DMethod) && !handlesInitializer)
					vis = MemberFilter.Types | MemberFilter.TypeParameters;

				return prv = new CtrlSpaceCompletionProvider(cdgen,scopedBlock, vis | ShownKeywords); 
			}
		}
		AbstractCompletionProvider prv;
		readonly ICompletionDataGenerator cdgen;
		readonly IEditorData ed;
		#endregion

		public CompletionProviderVisitor(ICompletionDataGenerator cdg, IEditorData ed, char enteredChar = '\0')
		{
			this.ed = ed;
			this.cdgen = cdg;
			this.triggerChar = enteredChar;
			this.shownKeywords.Push(BlockMemberFilter);
		}

		#region Nodes
		public override void VisitChildren (IBlockNode block)
		{
			if (!halt)
				shownKeywords.Push(BlockMemberFilter);

			var en = block.GetEnumerator ();
			while (!halt && en.MoveNext ()) {
				if (en.Current.Location > ed.CaretLocation) {
					halt = true;
					return;
				}
				en.Current.Accept (this);
			}

			if (!halt)
				shownKeywords.Pop ();
		}

		public override void VisitDNode (DNode n)
		{
			if (n.NameHash == DTokens.IncompleteIdHash) {
				if (n.ContainsAttribute(DTokens.Override))
					prv = new MethodOverrideCompletionProvider(n, cdgen);
				else
					explicitlyNoCompletion = true;
				halt = true;
			}
			else
				base.VisitDNode (n);
		}

		public override void Visit (DClassLike n)
		{
			if (!halt) {
				handlesBaseClasses = true;
				handledClass = n;
				foreach (var bc in n.BaseClasses)
					bc.Accept (this);
				handlesBaseClasses = false;

				if (!halt)
					VisitBlock (n);
			}
		}

		public override void Visit (DVariable n)
		{
			if (n.NameHash == 0 && n.ContainsAttribute(DTokens.Override))
			{
				prv = new MethodOverrideCompletionProvider(n, cdgen);
				halt = true;
				return;
			}

			if (n.IsAlias) {
				// alias |
				// alias id = |
				// NOT alias Type |
				if (IsIncompleteDeclaration (n.Type)) {
					prv = new CtrlSpaceCompletionProvider (cdgen, scopedBlock, MemberFilter.All | MemberFilter.BlockKeywords);
					halt = true;
					return;
				}
			}else if (n.Initializer != null) {
				initializedNode = n;
				handlesInitializer = true;
				n.Initializer.Accept (this);
				handlesInitializer = false;
			}

			if(!halt)
				VisitDNode(n);

			// auto |
			if(!halt && n.NameHash == 0 && 
				(n.ContainsAttribute (DTokens.Auto) || DParser.ContainsStorageClass(n.Attributes) != Modifier.Empty)) {
				halt = true;
				explicitlyNoCompletion = true;
			}
		}

		public override void Visit (TemplateAliasParameter p)
		{
			if (p.NameHash == DTokens.IncompleteIdHash) {
				halt = true;
				explicitlyNoCompletion = true;
			}
			else
				base.Visit (p);
		}

		public override void Visit (TemplateValueParameter p)
		{
			// Don't show on 'struct foo(int |' as well as on 'struct foo(|' 
			// - if a type is wanted, the user still may press ctrl+space
			if (p.NameHash == DTokens.IncompleteIdHash)
			{
				halt = true;
				explicitlyNoCompletion = true;
				return;
			}
			
			VisitTemplateParameter(p);

			if (!halt && p.Type != null)
				p.Type.Accept(this);

			if (!halt && p.SpecializationExpression != null) //TODO have a special completion case for specialization completion
				p.SpecializationExpression.Accept(this);

			if (!halt && p.DefaultExpression != null)
			{
				handlesInitializer = true;
				//initializedNode = p.Representation;
				p.DefaultExpression.Accept(this);
				handlesInitializer = false;
			}
		}
		#endregion

		#region TypeDeclarations
		public override void Visit (DTokenDeclaration td)
		{
			if (td.Token == DTokens.Incomplete) {
				if (handlesBaseClasses) {
					MemberFilter vis;
					if (handledClass.ClassType == DTokens.Interface)
						vis = MemberFilter.Interfaces | MemberFilter.Templates;
					else
						vis = MemberFilter.Classes | MemberFilter.Interfaces | MemberFilter.Templates;

					prv = new CtrlSpaceCompletionProvider (cdgen, handledClass, vis | MemberFilter.BlockKeywords);
				} else if (td.InnerDeclaration != null)
					prv = new MemberCompletionProvider (cdgen, td.InnerDeclaration, scopedBlock);
				else
					prv = new CtrlSpaceCompletionProvider (cdgen, scopedBlock, ShownKeywords);

				halt = true;
			} else
				base.Visit (td);
		}

		public override void Visit (IdentifierDeclaration td)
		{
			if (td.IdHash == DTokens.IncompleteIdHash) {
				halt = true;
				if(td.InnerDeclaration != null)
					prv = new MemberCompletionProvider (cdgen, td.InnerDeclaration, scopedBlock);
			}
			else
				base.Visit (td);
		}

		public static bool IsIncompleteDeclaration(ITypeDeclaration x)
		{
			if(x is DTokenDeclaration)
				return (x as DTokenDeclaration).Token == DTokens.Incomplete;
			if(x is IdentifierDeclaration)
				return (x as IdentifierDeclaration).IdHash == DTokens.IncompleteIdHash;
			return false;
		}
		#endregion

		#region Attributes
		public override void VisitAttribute (Modifier a)
		{
			string c;
			if (a.ContentHash == DTokens.IncompleteIdHash || ((c = a.LiteralContent as string) != null && c.EndsWith(DTokens.IncompleteId))) {
				prv = new AttributeCompletionProvider (a,cdgen);
				halt = true;
			}
			else
				base.VisitAttribute (a);
		}

		public override void Visit (ScopeGuardStatement s)
		{
			if (s.GuardedScope == DTokens.IncompleteId) {
				prv = new ScopeAttributeCompletionProvider (cdgen);
				halt = true;
			}
			else
				base.Visit (s);
		}

		public override void VisitAttribute (PragmaAttribute a)
		{
			if (a.Arguments != null && 
				a.Arguments.Length>0 &&
				IsIncompleteExpression (a.Arguments[a.Arguments.Length-1])) {
				prv = new PragmaCompletionProvider (a,cdgen);
				halt = true;
			}
			else
				base.VisitAttribute (a);
		}

		public override void VisitAttribute (UserDeclarationAttribute a)
		{
			if (a.AttributeExpression != null && 
				a.AttributeExpression.Length>0 &&
				IsIncompleteExpression (a.AttributeExpression[0])) {
				prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, 
					MemberFilter.BuiltInPropertyAttributes | MemberFilter.Methods | MemberFilter.Variables | MemberFilter.Types);
				halt = true;
			}
			else
				base.VisitAttribute (a);
		}

		public override void VisitAttribute (VersionCondition vis)
		{
			if (vis.VersionIdHash == DTokens.IncompleteIdHash) {
				halt = true;
				prv = new VersionSpecificationCompletionProvider (cdgen);
			}
			else
				base.VisitAttribute (vis);
		}

		public override void VisitAttribute (DebugCondition c)
		{
			if (c.DebugIdHash == DTokens.IncompleteIdHash) {
				halt = true;
				prv = new DebugSpecificationCompletionProvider(cdgen);
			}
			else
				base.VisitAttribute (c);
		}

		public override void VisitAttribute (StaticIfCondition a)
		{
			handlesInitializer = true;
			base.VisitAttribute (a);
			handlesInitializer = false;
		}
		#endregion

		#region Statements
		public override void VisitSubStatements(StatementContainingStatement stmtContainer)
		{
			if (halt)
				return;

			var ss = stmtContainer.SubStatements;
			if (ss != null) {
				shownKeywords.Push (BlockMemberFilter | MemberFilter.StatementBlockKeywords | ExprMemberFilter);
				
				foreach (IStatement substatement in ss)
					if (substatement != null) {
						substatement.Accept (this);
						if (halt)
							return;
					}

				if (!halt && stmtContainer.EndLocation < ed.CaretLocation)
					shownKeywords.Pop ();
			}
		}

		public override void VisitChildren(StatementContainingStatement stmtContainer)
		{
			VisitSubStatements(stmtContainer);
			if (!halt)
				VisitAbstractStmt (stmtContainer);
		}

		public override void VisitAbstractStmt (AbstractStatement stmt)
		{
			//base.VisitAbstractStmt (stmt);
		}

		public override void Visit(PostfixExpression_MethodCall x)
		{
			if (triggerChar == '(' && x.ArgumentCount > 0 && IsIncompleteExpression(x.Arguments[x.ArgumentCount - 1]))
			{
				halt = true;
				explicitlyNoCompletion = true;
			}
			else
				base.Visit(x);
		}

		public override void Visit (ModuleStatement s)
		{
			if (IsIncompleteDeclaration (s.ModuleName)) {
				prv = new ModuleStatementCompletionProvider (cdgen);
				halt = true;
			}
			else
				base.Visit (s);
		}

		ImportStatement.Import curImport;
		public override void VisitImport (ImportStatement.Import i)
		{
			if (IsIncompleteDeclaration(i.ModuleIdentifier))
			{
				prv = new ImportStatementCompletionProvider(cdgen, i);
				halt = true;
			}
			else
			{
				curImport = i;
				base.VisitImport(i);
			}
		}

		public override void VisitImport (ImportStatement.ImportBinding i)
		{
			if (!halt) {
				if (IsIncompleteDeclaration (i.Symbol)) {
					prv = new SelectiveImportCompletionProvider (cdgen, curImport);
					halt = true;
				} else
					base.VisitImport (i);
			}
		}

		public override void Visit(ForStatement s)
		{
			if (!halt)
			{
				base.Visit(s);
			}
		}

		public override void Visit (ForeachStatement s)
		{
			var decls = s.Declarations;
			if (decls != null && decls.Length > 0) {
				var lastDecl = decls [decls.Length - 1] as DNode; 
				if (lastDecl != null && lastDecl.NameHash == DTokens.IncompleteIdHash) {
					halt = true;
					// Probably a more common case to have 'auto |' not completed
					explicitlyNoCompletion = lastDecl.Type != null || (lastDecl.Attributes != null && lastDecl.Attributes.Count != 0);
					return;
				}
			}
			base.Visit (s);
		}

		public override void Visit (TemplateMixin s)
		{
			if (s.MixinId == DTokens.IncompleteId) {
				explicitlyNoCompletion = true;
				halt = true;
			}
			else
				base.Visit (s);
		}

		public override void Visit (BreakStatement s)
		{
			if(s.IdentifierHash == DTokens.IncompleteIdHash) {

				prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, MemberFilter.Labels);

				halt = true;
			}
			else
				base.Visit (s);
		}

		public override void Visit (ContinueStatement s)
		{
			if(s.IdentifierHash == DTokens.IncompleteIdHash) {

				prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, MemberFilter.Labels);

				halt = true;
			}
			else
				base.Visit (s);
		}

		public override void Visit (GotoStatement s)
		{
			if(s.StmtType == GotoStatement.GotoStmtType.Identifier &&
				s.LabelIdentifierHash == DTokens.IncompleteIdHash) {

				prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, MemberFilter.Labels);

				halt = true;
			}
			else
				base.Visit (s);
		}

		public override void Visit(AsmStatement s)
		{
			scopedStatement = s;
			base.Visit(s);
			scopedStatement = null;
		}

		public override void Visit(AsmStatement.InstructionStatement s)
		{
			scopedStatement = s;
			if (s.Operation == AsmStatement.InstructionStatement.OpCode.__UNKNOWN__)
			{
				prv = new InlineAsmCompletionProvider(s, cdgen);
				halt = true;
			}
			else
				base.Visit(s);
			scopedStatement = null;
		}

		public override void Visit(AsmStatement.RawDataStatement s)
		{
			scopedStatement = s;
			base.Visit(s);
			scopedStatement = null;
		}

		public override void Visit (VersionSpecification s)
		{
			if (s.SpecifiedId == DTokens.IncompleteId) {
				prv = new VersionSpecificationCompletionProvider(cdgen);
				halt = true;
			}
		}

		public override void Visit (DebugSpecification s)
		{
			if (s.SpecifiedId == DTokens.IncompleteId) {
				prv = new DebugSpecificationCompletionProvider(cdgen);
				halt = true;
			}
		}
		#endregion

		#region Expressions
		public override void VisitChildren (ContainerExpression x)
		{
			if (!halt) {
				shownKeywords.Push (ExprMemberFilter);
				base.VisitChildren (x);
				if (!halt)
					shownKeywords.Pop ();
			}
		}


		public override void Visit (TokenExpression e)
		{
			if (e.Token == DTokens.Incomplete) {
				halt = true;
				const MemberFilter BaseAsmFlags = MemberFilter.Classes | MemberFilter.StructsAndUnions | MemberFilter.Enums | MemberFilter.Methods | MemberFilter.TypeParameters | MemberFilter.Types | MemberFilter.Variables;
				if (scopedStatement is AsmStatement || scopedStatement is AsmStatement.InstructionStatement)
					prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, BaseAsmFlags | MemberFilter.x86Registers | MemberFilter.x64Registers | MemberFilter.Labels);
				else if (scopedStatement is AsmStatement.RawDataStatement)
					prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, BaseAsmFlags | MemberFilter.Labels);
				else /*if (handlesInitializer)*/ // Why only in initializers?
					prv = new CtrlSpaceCompletionProvider (cdgen, scopedBlock, shownKeywords.Count == 0 ? MemberFilter.All | MemberFilter.ExpressionKeywords : shownKeywords.Peek());
			}
		}

		public static bool IsIncompleteExpression(IExpression x)
		{
			return x is TokenExpression && (x as TokenExpression).Token == DTokens.Incomplete;
		}

		public override void Visit (PostfixExpression_Access x)
		{
			if (IsIncompleteExpression(x.AccessExpression)) {
				halt = true;
				if (x.PostfixForeExpression is TokenExpression && (x.PostfixForeExpression as TokenExpression).Token == DTokens.Dot)
				{
					// Handle module-scoped things:
					// When typing a dot without anything following, trigger completion and show types, methods and vars that are located in the module & import scope
					prv = new CtrlSpaceCompletionProvider (cdgen, scopedBlock, MemberFilter.Methods | MemberFilter.Types | MemberFilter.Variables | MemberFilter.TypeParameters );
				}
				else
					prv = new MemberCompletionProvider (cdgen, x.PostfixForeExpression, scopedBlock);
			}
			else
				base.Visit (x);
		}

		public override void Visit (TraitsExpression x)
		{
			if(x.Keyword == DTokens.IncompleteId)
			{
				prv = new TraitsExpressionCompletionProvider(cdgen);
				halt = true;
			}
			else
				base.Visit (x);
		}

		public override void Visit (NewExpression x)
		{
			if (IsIncompleteDeclaration (x.Type)) {
				halt = true;
				prv = new CtrlSpaceCompletionProvider (cdgen, scopedBlock, MemberFilter.Types | MemberFilter.TypeParameters);
			}
			else
				base.Visit (x);
		}

		public override void Visit (IsExpression x)
		{
			// is(Type |
			if (x.TypeAliasIdentifierHash == DTokens.IncompleteIdHash && 
				x.TestedType != null && 
				!IsIncompleteDeclaration(x.TestedType)) {
				halt = true;
				explicitlyNoCompletion = true;
			}
			else if (x.TypeSpecializationToken == DTokens.Incomplete)
			{
				prv = new CtrlSpaceCompletionProvider(cdgen, scopedBlock, MemberFilter.Types | MemberFilter.ExpressionKeywords | MemberFilter.StatementBlockKeywords);
				halt = true;
			}
			else
				base.Visit (x);
		}

		public override void Visit(StructInitializer init)
		{
			if (initializedNode != null && init.MemberInitializers != null && init.MemberInitializers.Length != 0)
			{
				var lastMemberInit = init.MemberInitializers[init.MemberInitializers.Length - 1];
				if (lastMemberInit.MemberNameHash == DTokens.IncompleteIdHash)
				{
					prv = new StructInitializerCompletion(cdgen,initializedNode, init);
					halt = true;
					return;
				}
			}

			base.Visit(init);
		}
		#endregion
	}
}

