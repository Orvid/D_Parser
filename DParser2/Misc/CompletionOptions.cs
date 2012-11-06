﻿using System.Xml;

namespace D_Parser.Misc
{
	public class CompletionOptions
	{
		public readonly static CompletionOptions Default = new CompletionOptions();


		public bool ShowUFCSItems = true;


		public void Load(XmlReader x)
		{
			while (x.Read())
			{
				switch (x.LocalName)
				{
					case "EnableUFCSCompletion":
						ShowUFCSItems = x.ReadString().ToLower() == "true";
						break;
				}
			}
		}

		public void Save(XmlWriter x)
		{
			x.WriteElementString("EnableUFCSCompletion", ShowUFCSItems.ToString());
		}
	}
}
