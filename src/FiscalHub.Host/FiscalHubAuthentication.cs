using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FiscalHub.Host;

/// <summary>
/// A autenticação do host, num ponto único. O teste de papel (design D8) sobe <b>esta</b> — e não uma cópia dela: o
/// 403 de um papel só prova alguma coisa se os claims, o esquema e a política de fallback forem os do host.
/// </summary>
public static class FiscalHubAuthentication
{
    /// <summary>
    /// Auth (JWT próprio): a chave/issuer/audience vêm da config. O host emite o token no <c>/auth/login</c> e valida o
    /// Bearer nas demais rotas. Em produção a chave é um segredo (Key Vault), não o appsettings.
    /// <para>Tudo exige autenticação por padrão (fallback policy); os endpoints públicos marcam <c>AllowAnonymous</c>.</para>
    /// </summary>
    public static IServiceCollection AddFiscalHubJwtAuthentication(this IServiceCollection services, JwtOptions jwt)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;   // mantém os claims com os nomes originais (tenant, role, name)
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
