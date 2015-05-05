#!/bin/sh

apt-get install mono-complete
mozroots --import --ask-remove

./start.sh
