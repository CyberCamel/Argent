using Argent.Core.Forms.Components.Base;
using System;
using System.Collections.Generic;

namespace Argent.Contracts
{
    public interface IComponentRegistry
    {
        /// <summary>
        /// Resolves the C# Type for a given JSON type string.
        /// </summary>
        Type Resolve(string typeName);

        /// <summary>
        /// Returns all registered metadata for the Form Builder toolbox.
        /// </summary>
        IEnumerable<string> GetRegisteredTypes();
    }
}
