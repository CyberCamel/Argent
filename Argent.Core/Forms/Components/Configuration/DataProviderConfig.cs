using Argent.Core.Forms.Filtering;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Forms.Components.Configuration
{
    public class DataProviderConfig
    {
        public string? DataSource { get; set; }
        public List<string> DependsOn { get; set; } = new();
        public QueryGroup? Filter { get; set; }
    }
}
