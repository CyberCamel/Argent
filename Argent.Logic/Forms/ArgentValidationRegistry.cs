using Argent.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic
{
    public class ArgentValidationRegistry : IValidationRegistry
    {
        public IValidationService GetService(string name)
        {
            throw new NotImplementedException();
        }

        public void Register(string name, IValidationService service)
        {
            throw new NotImplementedException();
        }
    }
}
