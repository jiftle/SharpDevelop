﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ICSharpCode.CodeCoverage
{
	public class CodeCoverageMethodElement
	{
		XElement element;
		CodeCoverageResults parent;

		public CodeCoverageMethodElement(XElement element, CodeCoverageResults parent)
		{
			this.parent = parent;
			this.element = element;
			this.SequencePoints = new List<CodeCoverageSequencePoint>();
			this.BranchPoints = new List<CodeCoverageBranchPoint>();
			Init();
		}
		private static string cacheFileName = String.Empty;
		private static CodeCoverageStringTextSource cacheDocument = null;

		public string FileID { get; private set; }
		public string FileName { get; private set; }
		public bool IsVisited { get; private set; }
		public int CyclomaticComplexity { get; private set; }
		public decimal SequenceCoverage { get; private set; }
		public int SequencePointsCount { get; private set; }
		public decimal BranchCoverage { get; private set; }
		public Tuple<int,int> BranchCoverageRatio { get; private set; }
		public bool IsConstructor { get; private set; }
		public bool IsStatic { get; private set; }
		public List<CodeCoverageSequencePoint> SequencePoints { get; private set; }
		public List<CodeCoverageBranchPoint> BranchPoints { get; private set; }

		public bool IsGetter { get; private set; }
		public bool IsSetter { get; private set; }
		public string MethodName { get; private set; }
		
		public bool IsProperty {
			get { return IsGetter || IsSetter; }
		}
		
		void Init()
		{
			MethodName = GetMethodName();
			IsGetter = GetBooleanAttributeValue("isGetter");
			IsSetter = GetBooleanAttributeValue("isSetter");

			this.FileID = GetFileRef();
			this.FileName = String.Empty;
			if (!String.IsNullOrEmpty(this.FileID)) {
				this.FileName = parent.GetFileName(this.FileID);
				if ( File.Exists(this.FileName) ) {
					if (cacheFileName != this.FileName) {
						cacheFileName = this.FileName;
						cacheDocument = null;
						try {
							using (Stream stream = new FileStream(this.FileName, FileMode.Open, FileAccess.Read)) {
								try {
									stream.Position = 0;
									string textSource = ICSharpCode.AvalonEdit.Utils.FileReader.ReadFileContent(stream, Encoding.Default);
									cacheDocument = new CodeCoverageStringTextSource(textSource);
								} catch {}
							}
						} catch {}
					}
				}
			}
			
			this.IsVisited = this.GetBooleanAttributeValue("visited");
			this.CyclomaticComplexity = (int)this.GetDecimalAttributeValue("cyclomaticComplexity");
			this.SequencePointsCount = this.GetSequencePointsCount();
			this.SequenceCoverage = (int)this.GetDecimalAttributeValue("sequenceCoverage");
			this.IsConstructor = this.GetBooleanAttributeValue("isConstructor");
			this.IsStatic = this.GetBooleanAttributeValue("isStatic");
			if ( !String.IsNullOrEmpty( this.FileID ) ) {
				this.SequencePoints = this.GetSequencePoints();
				this.BranchPoints = this.GetBranchPoints();
				this.BranchCoverageRatio = this.GetBranchRatio();
				this.BranchCoverage = this.GetBranchCoverage();
			}
		}
		
		List<CodeCoverageSequencePoint> GetSequencePoints() {

			List<CodeCoverageSequencePoint> sps = new List<CodeCoverageSequencePoint>();
			var xSPoints = this.element			
				.Elements("SequencePoints")
				.Elements("SequencePoint");

			foreach (XElement xSPoint in xSPoints) {
				CodeCoverageSequencePoint sp = new CodeCoverageSequencePoint();
				sp.FileID = this.FileID;
				sp.Document = this.FileName;
				sp.Line = (int)GetDecimalAttributeValue(xSPoint.Attribute("sl"));
				sp.EndLine = (int)GetDecimalAttributeValue(xSPoint.Attribute("el"));
				sp.Column = (int)GetDecimalAttributeValue(xSPoint.Attribute("sc"));
				sp.EndColumn = (int)GetDecimalAttributeValue(xSPoint.Attribute("ec"));
				sp.VisitCount = (int)GetDecimalAttributeValue(xSPoint.Attribute("vc"));
				if (cacheFileName == sp.Document && cacheDocument != null) {
					sp.Content = cacheDocument.GetText(sp);
					if (sp.Line != sp.EndLine) {
						sp.Content = Regex.Replace (sp.Content, @"\s+", " ");
					}
					sp.Length = Regex.Replace (sp.Content, @"\s", "").Length; // ignore white-space for coverage%
				} else {
					sp.Content = String.Empty;
					sp.Length = 0;
				}
				sp.Offset = (int)GetDecimalAttributeValue(xSPoint.Attribute("offset"));
				sp.BranchCoverage = true;

				sps.Add(sp);
			}
			return sps;
		}

		int GetSequencePointsCount() {
			XElement summary = this.element.Element("Summary");
			if ( summary != null ) {
				XAttribute nsp = summary.Attribute("numSequencePoints");
				if ( nsp != null ) {
					return (int)GetDecimalAttributeValue( nsp );
				}
			}
			return 0;
		}

		List<CodeCoverageBranchPoint> GetBranchPoints() {
			// get all BranchPoints
			List<CodeCoverageBranchPoint> bps = new List<CodeCoverageBranchPoint>();
			var xBPoints = this.element			
				.Elements("BranchPoints")
				.Elements("BranchPoint");
			foreach (XElement xBPoint in xBPoints) {
				CodeCoverageBranchPoint bp = new CodeCoverageBranchPoint();
				bp.VisitCount = (int)GetDecimalAttributeValue(xBPoint.Attribute("vc"));
				bp.Offset = (int)GetDecimalAttributeValue(xBPoint.Attribute("offset"));
				bp.Path = (int)GetDecimalAttributeValue(xBPoint.Attribute("path"));
				bp.OffsetEnd = (int)GetDecimalAttributeValue(xBPoint.Attribute("offsetend"));
				bps.Add(bp);
			}
			return bps;
		}
		
		// Find method-body first SequencePoint "{"
		public static CodeCoverageSequencePoint getBodyStartSP(IEnumerable<CodeCoverageSequencePoint> sps) {
			CodeCoverageSequencePoint startSeqPoint = null;
			foreach (CodeCoverageSequencePoint sp in sps) {
				if ( sp.Content == "{") {
					startSeqPoint = sp;
					break;
				}
			}
			return startSeqPoint;
		}

		// Find method-body final SequencePoint "}" 
		public static CodeCoverageSequencePoint getBodyFinalSP(IEnumerable<CodeCoverageSequencePoint> sps) {
			CodeCoverageSequencePoint finalSeqPoint = null;
			foreach (CodeCoverageSequencePoint sp in Enumerable.Reverse(sps)) {
				if ( sp.Content == "}") {
					finalSeqPoint = sp;
					break;
				}
			}
			return finalSeqPoint;
		}
		
		Tuple<int,int> GetBranchRatio () {

			// goal: Get branch ratio and exclude (rewriten) Code Contracts branches 

			if ( this.BranchPoints == null 
				|| this.BranchPoints.Count() == 0 
				|| this.SequencePoints == null
				|| this.SequencePoints.Count == 0
			   )
			{
				return null;
			}

			// This sequence point offset is used to skip CCRewrite(n) BranchPoint's (Requires)
			// and '{' branches at static methods
			CodeCoverageSequencePoint startSeqPoint = getBodyStartSP(this.SequencePoints);
			Debug.Assert (!Object.ReferenceEquals(null, startSeqPoint));
			if (Object.ReferenceEquals(null, startSeqPoint)) { return null; }

			// This sequence point offset is used to skip CCRewrite(n) BranchPoint's (Ensures)
			CodeCoverageSequencePoint finalSeqPoint = getBodyFinalSP(this.SequencePoints);
			Debug.Assert ( !Object.ReferenceEquals( null, finalSeqPoint) );
			if (Object.ReferenceEquals(null, finalSeqPoint)) { return null; }
			
			// Connect Sequence & Branches
			IEnumerator<CodeCoverageSequencePoint> SPEnumerator = this.SequencePoints.GetEnumerator();
			CodeCoverageSequencePoint currSeqPoint = startSeqPoint;
			int nextSeqPointOffset = startSeqPoint.Offset;
			
			foreach (var bp in this.BranchPoints) {
			    
			    // ignore branches outside of method body
				if (bp.Offset < startSeqPoint.Offset)
					continue;
				if (bp.Offset > finalSeqPoint.Offset)
					break;

				// Sync with SequencePoint
				while ( nextSeqPointOffset < bp.Offset ) {
					currSeqPoint = SPEnumerator.Current;
					if ( SPEnumerator.MoveNext() ) {
						nextSeqPointOffset = SPEnumerator.Current.Offset;
					} else {
						nextSeqPointOffset = int.MaxValue;
					}
				}
    			if (currSeqPoint.Branches == null) {
    			    currSeqPoint.Branches = new List<CodeCoverageBranchPoint>();
    			}
				// Add Branch to Branches
				currSeqPoint.Branches.Add(bp);
			}

			// Merge sp.Branches on exit-offset
			// Calculate Method Branch coverage
			int totalBranchVisit = 0;
			int totalBranchCount = 0;
			int pointBranchVisit = 0;
			int pointBranchCount = 0;
			Dictionary<int, CodeCoverageBranchPoint> bpExits = new Dictionary<int, CodeCoverageBranchPoint>();
			foreach (var sp in this.SequencePoints) {

			    // SequencePoint covered & has branches?
			    if (sp.VisitCount != 0 && sp.Branches != null) {

        			// 1) Generated "in" code for IEnumerables contains hidden "try/catch/finally" branches that
        			// one do not want or cannot cover by test-case because is handled earlier at same method.
        			// ie: NullReferenceException in foreach loop is pre-handled at method entry, ie. by Contract.Require(items!=null)
        			// 2) Branches within sequence points "{" and "}" are not source branches but compiler generated branches
        			// ie: static methods start sequence point "{" contains compiler generated branches
        			// 3) Exclude Contract class (EnsuresOnThrow/Assert/Assume is inside method body)
        			// 4) Exclude NUnit Assert(.Throws) class
			        if (sp.Content == "in" || sp.Content == "{" || sp.Content == "}" ||
			            sp.Content.StartsWith("Assert.") ||
			            sp.Content.StartsWith("Assert ") ||
			            sp.Content.StartsWith("Contract.") ||
			            sp.Content.StartsWith("Contract ")
			           ) {
			            sp.Branches = null;
			            continue; // skip
			        }

				    // Merge sp.Branches on OffsetEnd using bpExits key
			        bpExits.Clear();
    			    foreach (var bp in sp.Branches) {
			            if (!bpExits.ContainsKey(bp.OffsetEnd)) {
			                bpExits[bp.OffsetEnd] = bp; // insert branch
			            } else {
			                bpExits[bp.OffsetEnd].VisitCount += bp.VisitCount; // update branch
			            }
    			    }

				    // Compute branch coverage
				    pointBranchVisit = 0;
			        pointBranchCount = 0;
			        foreach (var bp in bpExits.Values) {
			            pointBranchVisit += bp.VisitCount == 0? 0 : 1 ;
						pointBranchCount += 1;
			        }
		            // Not full coverage?
		            if (pointBranchVisit != pointBranchCount) {
   			            sp.BranchCoverage = false; // => part-covered
		            }
					totalBranchVisit += pointBranchVisit;
					totalBranchCount += pointBranchCount;
			    }
			    if (sp.Branches != null)
				    sp.Branches = null; // release memory
			}

			return (totalBranchCount!=0) ? new Tuple<int,int>(totalBranchVisit,totalBranchCount) : null;
			
		}

		decimal GetBranchCoverage () {
			
			return this.BranchCoverageRatio != null ? decimal.Round( ((decimal)(this.BranchCoverageRatio.Item1*100))/(decimal)this.BranchCoverageRatio.Item2, 2) : 0m;
			
		}

		decimal GetDecimalAttributeValue(string name)
		{
			return GetDecimalAttributeValue(element.Attribute(name));
		}
		
		decimal GetDecimalAttributeValue(XAttribute attribute)
		{
			if (attribute != null) {
				decimal value = 0;
				if (Decimal.TryParse(attribute.Value, out value)) {
					return value;
				}
			}
			return 0;
		}
		
		bool GetBooleanAttributeValue(string name)
		{
			return GetBooleanAttributeValue(element.Attribute(name));
		}
		
		bool GetBooleanAttributeValue(XAttribute attribute)
		{
			if (attribute != null) {
				bool value = false;
				if (Boolean.TryParse(attribute.Value, out value)) {
					return value;
				}
			}
			return false;
		}

		string GetFileRef() {
			XElement fileId = element.Element("FileRef");
			if (fileId != null) {
				return fileId.Attribute("uid").Value;
			}
			return String.Empty;
		}

		string GetMethodName()
		{
			XElement nameElement = element.Element("Name");
			if (nameElement != null) {
				return GetMethodName(nameElement.Value);
			}
			return String.Empty;
		}
		
		string GetMethodName(string methodSignature)
		{
			int startIndex = methodSignature.IndexOf("::");
			int endIndex = methodSignature.IndexOf('(', startIndex);
			return methodSignature
				.Substring(startIndex, endIndex - startIndex)
				.Substring(2);
		}
	}
}
