use std::net::SocketAddr;

use tokio::{net::UdpSocket, time::Instant};

use crate::AppState;

pub async fn relay_server(bind: SocketAddr, st: AppState) -> anyhow::Result<()> {
    const RX_BUF_SIZE: usize = 2048;
    let sock = UdpSocket::bind(bind).await?;
    let mut rx = [0u8; RX_BUF_SIZE];

    loop {
        let (n, src) = sock.recv_from(&mut rx).await?;

        tracing::debug!("Received relay request from address {}", src);

        let mut state = st.udp_state.write().await;
        let Some(client_id) = state.addr_to_client.get(&src).copied() else {
            // not registered
            continue;
        };
        let Some(ep) = state.udp_addrs.get_mut(&client_id) else {
            continue;
        };
        ep.last_seen = Instant::now();

        let state = state.downgrade();
        let peer_ep = {
            let inner = st.room_state.read().await;
            let Some(peer) = inner.get_peer(client_id) else {
                continue;
            };
            let Some(peer_ep) = state.udp_addrs.get(&peer) else {
                continue;
            };
            peer_ep
        };
        tracing::debug!(
            "Forwarding relay request from client {} to addr {}",
            client_id,
            peer_ep.udp_addr
        );
        let _ = sock.send_to(&rx[..n], peer_ep.udp_addr).await;
    }
}
