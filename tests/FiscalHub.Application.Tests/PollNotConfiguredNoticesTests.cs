using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica quando o aviso de "poll desligado por ausência de configuração" é repetido: logo na primeira passada, de
/// novo a cada intervalo enquanto o problema continuar, e de novo na hora se ele voltar depois de corrigido. Sem
/// repetir, o aviso some do log do dia; repetindo a cada tick de 15 s, ele afoga o resto.
/// </summary>
public class PollNotConfiguredNoticesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_pass_warns_every_tenant_without_the_section()
    {
        var notices = new PollNotConfiguredNotices(TimeSpan.FromHours(1));

        Assert.Equal(["tenant-a", "tenant-b"], notices.Due(["tenant-a", "tenant-b"], T0));
    }

    [Fact]
    public void Within_the_interval_it_does_not_repeat_and_after_it_it_does()
    {
        var notices = new PollNotConfiguredNotices(TimeSpan.FromHours(1));
        notices.Due(["tenant-a"], T0);

        Assert.Empty(notices.Due(["tenant-a"], T0.AddMinutes(59)));
        Assert.Equal(["tenant-a"], notices.Due(["tenant-a"], T0.AddHours(1)));
        Assert.Empty(notices.Due(["tenant-a"], T0.AddHours(1).AddMinutes(1)));
    }

    [Fact]
    public void A_tenant_that_is_fixed_and_breaks_again_is_warned_at_once()
    {
        var notices = new PollNotConfiguredNotices(TimeSpan.FromHours(1));
        notices.Due(["tenant-a"], T0);

        Assert.Empty(notices.Due([], T0.AddMinutes(5)));                          // corrigido
        Assert.Equal(["tenant-a"], notices.Due(["tenant-a"], T0.AddMinutes(10)));   // voltou: avisa sem esperar a hora
    }

    [Fact]
    public void The_message_says_why_it_is_off_where_it_comes_from_and_how_to_turn_it_on()
    {
        string message = PollNotConfiguredNotices.Message;

        Assert.Contains("{Tenant}", message);
        Assert.Contains("{Origin}", message);
        Assert.Contains("desligado por ausência de configuração", message);
        Assert.Contains("tela antiga", message);
        Assert.Contains("poll.enabled", message);
    }
}
