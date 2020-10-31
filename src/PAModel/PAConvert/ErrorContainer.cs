// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.Formulas.Tools.IR;
using Microsoft.PowerPlatform.Formulas.Tools.Parser;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.PowerPlatform.Formulas.Tools
{
    internal class ErrorContainer
    {
        private List<PAError> _errors;

        public ErrorContainer()
        {
            _errors = new List<PAError>();
        }

        internal void AddError(SourceLocation span, string errorMessage)
        {
            _errors.Add(new PAError(span, errorMessage));
        }

        public bool HasErrors()
        {
            return _errors.Any();
        }

        public IEnumerable<PAError> Errors()
        {
            return _errors.AsEnumerable();
        }
    }
}
