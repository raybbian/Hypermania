use std::net::SocketAddr;
use tokio::{net::UdpSocket, time::Instant};

use crate::{
    AppState,
    utils::{UdpClientState, parse_client_id},
};

#[repr(u8)]
enum OutgoingPacketType {
    FoundPeer = 0x1,
    WaitingPeer = 0x2,
}

fn encode_waiting<'a>(out: &'a mut [u8]) -> &'a [u8] {
    out[0] = OutgoingPacketType::WaitingPeer as u8;
    &out[..1]
}

fn encode_socket<'a>(peer: SocketAddr, out: &'a mut [u8]) -> &'a [u8] {
    out[0] = OutgoingPacketType::FoundPeer as u8;
    match peer.ip() {
        std::net::IpAddr::V4(v4) => {
            // total 8 bytes
            out[1] = 4;
            out[2..6].copy_from_slice(&v4.octets());
            out[6..8].copy_from_slice(&peer.port().to_be_bytes());
            &out[..8]
        }
        std::net::IpAddr::V6(v6) => {
            // total 20 bytes
            out[1] = 6;
            out[2..18].copy_from_slice(&v6.octets());
            out[18..20].copy_from_slice(&peer.port().to_be_bytes());
            &out[..20]
        }
    }
}

pub async fn udp_coordinator(bind: SocketAddr, st: AppState) -> anyhow::Result<()> {
    const RX_BUF_SIZE: usize = 2048;
    const TX_BUF_SIZE: usize = 64;

    let sock = UdpSocket::bind(bind).await?;
    let mut rx = [0u8; RX_BUF_SIZE];
    let mut tx = [0u8; TX_BUF_SIZE];

    loop {
        let (n, src) = sock.recv_from(&mut rx).await?;

        let Some(client_id) = parse_client_id(&rx[..n]) else {
            continue;
        };

        tracing::debug!(
            "Received udp from client {} from address {}",
            client_id,
            src
        );
        let _ = {
            let now = Instant::now();
            let mut udp_state = st.udp_state.write().await;
            match udp_state.udp_addrs.remove(&client_id) {
                Some(mut ep) => {
                    if ep.udp_addr != src {
                        let old = ep.udp_addr;
                        tracing::debug!(
                            "Udp client {client_id} migrated from address {} to {}",
                            old,
                            src
                        );

                        udp_state.addr_to_client.remove(&old);
                        udp_state.addr_to_client.insert(src, client_id);
                        ep.udp_addr = src;
                    }
                    ep.last_seen = now;

                    udp_state.udp_addrs.insert(client_id, ep);
                }
                None => {
                    udp_state.udp_addrs.insert(
                        client_id,
                        UdpClientState {
                            udp_addr: src,
                            last_seen: now,
                        },
                    );
                    udp_state.addr_to_client.insert(src, client_id);
                }
            }
        };

        let maybe_pairs: Option<[(SocketAddr, SocketAddr); 2]> = {
            let room_state = st.room_state.read().await;
            let udp_state = st.udp_state.read().await;
            let Some(client) = room_state.clients.get(&client_id) else {
                continue;
            };
            let Some(room) = room_state.rooms.get(&client.room) else {
                continue;
            };
            let Some(guest_id) = room.client else {
                continue;
            };
            let host_id = room.host;
            let Some(host_udp) = udp_state.udp_addrs.get(&host_id) else {
                continue;
            };
            let Some(guest_udp) = udp_state.udp_addrs.get(&guest_id) else {
                continue;
            };
            Some([
                (host_udp.udp_addr, guest_udp.udp_addr),
                (guest_udp.udp_addr, host_udp.udp_addr),
            ])
        };

        if let Some(pairs) = maybe_pairs {
            for (dst, peer) in pairs {
                tracing::debug!("Forwarding punch peer {} for client {}", peer, client_id);

                let pkt = encode_socket(peer, &mut tx);
                let _ = sock.send_to(pkt, dst).await;
            }
        } else {
            let pkt = encode_waiting(&mut tx);
            let _ = sock.send_to(pkt, src).await;
        }
    }
}
