using Argent.Contracts.Forms;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Forms
{
    public class ArgentFormValidatorRegistry : IFormValidatorRegistry
    {
        public IFormValidationService GetService(string name)
        {
            throw new NotImplementedException();
        }

        public void Register(string name, IFormValidationService service)
        {
            throw new NotImplementedException();
        }
    }
}
