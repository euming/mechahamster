FROM ubuntu:18.04

RUN useradd -m -s /bin/bash loadsim
USER loadsim

WORKDIR /home/loadsim
COPY ./mecha-load-test /home/loadsim/mecha-load-test

CMD ["./mecha-load-test"]
