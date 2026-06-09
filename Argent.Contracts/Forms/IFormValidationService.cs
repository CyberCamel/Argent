using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms
{
    public interface IFormValidationService
    {
        public bool Validate(string value);
    }
}
