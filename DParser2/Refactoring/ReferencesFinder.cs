//
// NewReferencesFinder.cs
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
using System.Collections.Generic;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver;
using D_Parser.Resolver.ASTScanner;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.TypeResolution;
using System.Linq;

namespace D_Parser.Refactoring
{
	public class ReferencesFinder : AbstractResolutionVisitor
	{
		#region Properties
		readonly List<ISyntaxRegion> l = new List<ISyntaxRegion>();
		readonly INode symbol;
		readonly int searchHash;
		#endregion

		#region Constructor / External
		ReferencesFinder(INode symbol, DModule ast, ResolutionContext ctxt) : base(ctxt)
		{
			this.symbol = symbol;
			searchHash = symbol.NameHash;
		}

		public static IEnumerable<ISyntaxRegion> Scan(INode symbol, ResolutionContext ctxt, bool includeDefinition = true)
		{
			return Scan(symbol.NodeRoot as DModule, symbol, ctxt, includeDefinition);
		}

		/// <summary>
		/// </summary>
		/// <param name="ast">The syntax tree to scan</param>
		/// <param name="symbol">Might not be a child symbol of ast</param>
		/// <param name="ctxt">The context required to search for symbols</param>
		/// <returns></returns>
		public static IEnumerable<ISyntaxRegion> Scan(DModule ast, INode symbol, ResolutionContext ctxt, bool includeDefinition = true)
		{
			if (ast == null || symbol == null || ctxt == null)
				return null;

			var f = new ReferencesFinder(symbol, ast, ctxt);

			using(ctxt.Push(ast))
				ast.Accept (f);

			var nodeRoot = symbol.NodeRoot as DModule;
			if (includeDefinition && nodeRoot != null && nodeRoot.FileName == ast.FileName)
			{
				var dc = symbol.Parent as DClassLike;
				if (dc != null && dc.ClassType == D_Parser.Parser.DTokens.Template &&
					dc.NameHash == symbol.NameHash)
				{
					f.l.Insert(0, new IdentifierDeclaration(dc.NameHash)
						{
							Location = dc.NameLocation,
							EndLocation = new CodeLocation(dc.NameLocation.Column + dc.Name.Length, dc.NameLocation.Line)
						});
				}

				var loc = symbol.NameLocation;
				bool add = !f.l.AsParallel().Any(
					(o) =>	(o is TemplateParameter && (o as TemplateParameter).NameLocation == loc) ||
							(o is INode && (o as INode).NameLocation == loc));

				if(add)
					f.l.Insert(0, new IdentifierDeclaration(symbol.NameHash)
						{
							Location = loc,
							EndLocation = new CodeLocation(loc.Column + symbol.Name.Length,	loc.Line)
						});
			}

			return f.l;
		}
		#endregion
		/*
		/// <summary>
		/// Used to extract the adequate code location + the identifier length
		/// </summary>
		public static CodeLocation ExtractIdLocation(ISyntaxRegion sr, out int idLength)
		{
			if (sr is IdentifierDeclaration)
			{
				var id = (IdentifierDeclaration)sr;

				idLength = id.Id.Length;
				return id.Location;
			}
			else if (sr is IdentifierExpression)
			{
				var id = (IdentifierExpression)sr;
				idLength = id.StringValue.Length;
				return id.Location;
			}
			else if (sr is TemplateInstanceExpression)
			{
				var tix = (TemplateInstanceExpression)sr;
				idLength = tix.TemplateId.Length;
				return tix.Identifier.Location;
			}
			else if (sr is PostfixExpression_Access)
				return ExtractIdLocation(((PostfixExpression_Access)sr).AccessExpression, out idLength);
			else if (sr is NewExpression)
				return ExtractIdLocation(((NewExpression)sr).Type, out idLength);

			idLength = 0;
			return CodeLocation.Empty;
		}
		*/

		#region Id filter visit overloads
		bool resolveAnyway = false;
		public override void VisitTemplateParameter (TemplateParameter tp)
		{
			if (tp.NameHash == searchHash && 
				tp.Representation == symbol)
				l.Add (tp);
		}

		public override void Visit (IdentifierDeclaration id)
		{
			if (id.IdHash == searchHash || resolveAnyway) {
				ctxt.CurrentContext.Set(id.Location);
				if (TryAdd(TypeDeclarationResolver.ResolveSingle(id, ctxt), id))
					return;
			}

			base.Visit (id);
		}

		bool TryAdd(AbstractType resolvedSymbol, ISyntaxRegion sr)
		{
			if (resolvedSymbol != null)
			{
				INode n;
				var aliasTag = resolvedSymbol.Tag as D_Parser.Resolver.TypeResolution.TypeDeclarationResolver.AliasTag;
				if (aliasTag != null)
					n = aliasTag.aliasDefinition;
				else if (resolvedSymbol is DSymbol)
					n = (resolvedSymbol as DSymbol).Definition;
				else
					n = null;

				if (n == symbol)
				{
					l.Add(sr);
					return true;
				}
			}
			return false;
		}

		public override void Visit (IdentifierExpression id)
		{
			if (id.IsIdentifier && id.ValueStringHash == searchHash)
			{
				ctxt.CurrentContext.Set(id.Location);
				if (TryAdd(ExpressionTypeEvaluation.EvaluateType(id, ctxt, false), id))
					return;
			}
		}

		public override void Visit (TemplateInstanceExpression id)
		{
			if (id.TemplateIdHash == searchHash)
			{
				ctxt.CurrentContext.Set(id.Location);
				if (TryAdd(ExpressionTypeEvaluation.EvaluateType(id, ctxt, false), id))
					return;
			}
		}

		public override void Visit (PostfixExpression_Access acc)
		{
			acc.PostfixForeExpression.Accept(this);

			if ((acc.AccessExpression is IdentifierExpression &&
			    (acc.AccessExpression as IdentifierExpression).ValueStringHash != searchHash) ||
			    (acc.AccessExpression is TemplateInstanceExpression &&
			    ((TemplateInstanceExpression)acc.AccessExpression).TemplateIdHash != searchHash)) {
				return;
			} else if (acc.AccessExpression is NewExpression) {
				var nex = acc.AccessExpression as NewExpression;

				if ((nex.Type is IdentifierDeclaration &&
				    ((IdentifierDeclaration)nex.Type).IdHash != searchHash) ||
				    (nex.Type is TemplateInstanceExpression &&
				    ((TemplateInstanceExpression)acc.AccessExpression).TemplateIdHash != searchHash)) {
 					return;
				}
				// Are there other types to test for?
			} else {
				// Are there other types to test for?
 			}

			ctxt.CurrentContext.Set(acc.Location);
			var ov = ExpressionTypeEvaluation.GetAccessedOverloads(acc, ctxt, null, false);
			if (ov == null)
				return;

			foreach(var o in ov)
				if (TryAdd(o, acc.AccessExpression))
					return;
		}

		public override void Visit (IsExpression x)
		{
			if (x.TypeAliasIdentifierHash == searchHash && 
				symbol is TemplateParameter.Node &&
				(symbol as TemplateParameter.Node).TemplateParameter == x.ArtificialFirstSpecParam) {
				l.Add (x.ArtificialFirstSpecParam);
			}
			base.Visit (x);
		}
		#endregion
	}
}

