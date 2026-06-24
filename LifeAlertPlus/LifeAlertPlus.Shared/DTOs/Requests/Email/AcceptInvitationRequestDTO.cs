namespace LifeAlertPlus.Shared.DTOs.Requests.Email
{
    // Server: primit de InvitationsController.AcceptInvitation (POST /api/invitations/accept, necesită [Authorize]) —
    // permite unui medic CU CONT să transforme o invitație temporară (acces prin token, fără login) într-un
    // acces permanent legat de contul lui (emailul din JWT trebuie să coincidă cu emailul invitat).
    // NOTĂ: la momentul acestui comentariu, niciun ecran din Client nu apelează acest endpoint — fluxul activ
    // de acceptare a invitațiilor e cel anonim prin token (InviteAcceptPage.razor.cs, fără autentificare).
    public class AcceptInvitationRequestDTO
    {
        public string Token { get; set; } = string.Empty;
    }
}
