using Unity.Netcode.Components;

// Acest script spune rețelei: "Lasă jucătorul să decidă ce animație joacă, nu doar serverul"
public class ClientNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
