using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums
{
    public static class Incoterm
    {
        public const string EXW = "EXW";//工厂交货
        public const string FOB = "FOB";//船上交货
        public const string CIF = "CIF";//成本+保险+运费
        public const string DAP = "DAP";//目的地交货
        public const string DDP = "DDP";//完税后交货
        public const string CPT = "CPT";//运费付至
        public const string CIP = "CIP";//运费和保险费付至

    }
}
