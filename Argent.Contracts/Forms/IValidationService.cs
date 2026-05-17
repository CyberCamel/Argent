using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms
{
    public interface IValidationService
    {
        public bool Validate(string value);
    }
}
