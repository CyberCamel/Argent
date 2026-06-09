using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms
{
    public interface IFormValidatorRegistry
    {

        public IFormValidationService GetService(string name);

        public void Register(string name, IFormValidationService service);

    }
}
