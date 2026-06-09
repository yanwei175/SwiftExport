using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.Wrappers
{
    public class SaveDocumentRequestBase<THead, TLine>
    {
        public THead Header { get; set; } 
        public List<TLine> AddLines { get; set; } = new List<TLine>();
        public List<TLine> UpdateLines { get; set; } = new List<TLine>();
        public List<Guid> DeleteLineIds { get; set; } = new List<Guid>();

        public bool HasChanges =>
            AddLines.Count > 0 ||
            UpdateLines.Count > 0 ||
            DeleteLineIds.Count > 0;
    }
}
