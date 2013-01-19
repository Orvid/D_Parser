﻿using System;
using D_Parser.Dom;

namespace D_Parser.Formatting
{
	public interface IDocumentAdapter
	{
		char this[int offset]{get;}
		int ToOffset(CodeLocation loc);
		int ToOffset(int line, int column);
		CodeLocation ToLocation(int offset);
		int TextLength{get;}
		string Text{get;}
	}
	
	public class TextDocument : IDocumentAdapter
	{
		string text = string.Empty;
		public string Text{get{return text;} set{text = value;}}
		public char this[int o] { get{ return text[o]; } }
		
		public int TextLength {
			get {
				return text == null ? 0 : text.Length;
			}
		}
		
		public int ToOffset(CodeLocation loc)
		{
			return DocumentHelper.LocationToOffset(text, loc.Line, loc.Column);
		}
		
		public int ToOffset(int line, int column)
		{
			return DocumentHelper.LocationToOffset(text,line, column);
		}
		
		public CodeLocation ToLocation(int offset)
		{
			return DocumentHelper.OffsetToLocation(text, offset);
		}
	}
}