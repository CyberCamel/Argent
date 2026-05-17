using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Forms.Filtering;

internal class FilterItem : IFilter
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "==";
    public string? Source { get; set; }
    public object? Value { get; set; }

    
    public string Build() {
        throw new NotImplementedException();
    }
}
