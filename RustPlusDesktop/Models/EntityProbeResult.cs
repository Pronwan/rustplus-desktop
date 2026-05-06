using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustPlusDesk.Models;

public readonly record struct EntityProbeResult(bool Exists, string? Kind, bool? IsOn);
