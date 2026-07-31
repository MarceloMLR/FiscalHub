namespace FiscalHub.Application.Admin;

/// <summary>Resultado de uma operação administrativa, traduzido em HTTP status na borda.</summary>
public enum AdminStatus
{
    Ok,
    NotFound,
    Conflict,
    Invalid,
}

/// <summary>Envelope de resultado com valor opcional e mensagem para o caso de erro.</summary>
public sealed record AdminResult<T>(AdminStatus Status, T? Value = default, string? Message = null)
    where T : class
{
    public static AdminResult<T> Success(T value) => new(AdminStatus.Ok, value);
    public static AdminResult<T> Fail(AdminStatus status, string message) => new(status, null, message);
}

/// <summary>Visão de um usuário para a tela de administração (nunca expõe hash de senha).</summary>
public sealed record AdminUserView(int Id, string Email, string Name, string Role, bool Active);

/// <summary>Dados para criar um usuário dentro do tenant corrente.</summary>
public sealed record CreateUserInput(string Email, string Name, string Role, string Password);

/// <summary>Alterações parciais em um usuário: campos nulos ficam como estão.</summary>
public sealed record UpdateUserInput(string? Name, string? Role, bool? Active);

/// <summary>Cadastro (fino) do tenant corrente.</summary>
public sealed record TenantView(string TenantId, string Name, string? Cnpj, bool Active);

/// <summary>Alterações nos dados cadastrais do tenant.</summary>
public sealed record UpdateTenantInput(string Name, string? Cnpj);
