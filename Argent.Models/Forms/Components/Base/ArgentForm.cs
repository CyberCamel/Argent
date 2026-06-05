using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Forms.Components.Base
{
    internal class ArgentForm
    {
        public required string Title { get; set; }
        public string? Description { get; set; }
        public required int Version { get; set; }
        public required List<FormComponent> Components  { get; set; }
    }
}
