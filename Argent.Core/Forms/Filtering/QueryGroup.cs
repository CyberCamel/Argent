using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Forms.Filtering;


public enum QueryGroupLogic
{
    AND,
    OR
}

public class QueryGroup : IFilter
{
    public QueryGroupLogic Logic { get; set; } = QueryGroupLogic.AND;
    public IFilter[] Filters { get; set; } = [];

    public string Build()
    {
        throw new NotImplementedException();
    }
}
