﻿using Shesha.DynamicEntities.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shesha.DynamicEntities
{
    public interface IHasDisplayNameField
    {
        string _displayName { get; }
    }
}
