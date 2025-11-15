using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LifeAlertPlus.Domain.Entities;

namespace LifeAlertPlus.Application.IServices
{
    public interface IJwtService
    {
        string GenerateToken(User user);
    }
}
