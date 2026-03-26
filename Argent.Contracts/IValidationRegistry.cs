using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts
{
    internal interface IValidationRegistry
    {

        public IValidationService GetService(string name);

        public void Register(string name, IValidationService service);

    }
}
